"""Atlas packer.

Shelf packing: sort sprites by height descending, fill rows up to a maximum
width, stack rows downward. When the next row would exceed the height cap the
current page is finalized and packing continues on a fresh page. This keeps
every page within the 16384 px GPU/Unity texture limit; oversized characters
simply spill onto ``atlas_1.png``, ``atlas_2.png`` and so on.

Produces:
    atlas.png / atlas_1.png ... - the packed RGBA atlas pages
    atlas.sprites.json          - per-sprite rect + pivot + page info
"""

from __future__ import annotations

import json
import os
from dataclasses import dataclass, field
from typing import Optional

from PIL import Image

from .sff_v1 import SffSprite

# Hard maximum texture dimension supported by common GPUs / Unity. A texture
# exceeding this on any axis is rejected at import time, so no page may grow
# past it — instead we spill onto additional pages.
MAX_TEXTURE_DIM = 16384


@dataclass
class PackedSprite:
    key: str            # "group_image"
    group: int
    image: int
    x: int              # atlas x
    y: int              # atlas y
    w: int
    h: int
    pivot_x: int        # MUGEN x_axis (relative to top-left of sprite)
    pivot_y: int        # MUGEN y_axis
    page: int = 0       # atlas page index (0 = atlas.png, 1 = atlas_1.png, ...)


@dataclass
class PackedAtlas:
    width: int
    height: int
    sprites: list[PackedSprite] = field(default_factory=list)
    image: Optional[Image.Image] = None

    def by_key(self) -> dict[str, PackedSprite]:
        return {s.key: s for s in self.sprites}


def pack_sprites(
    sprites: list[SffSprite],
    *,
    max_width: int = 8192,
    max_height: int = MAX_TEXTURE_DIM,
    padding: int = 2,
    transparent_index: int = 0,
) -> list[PackedAtlas]:
    """Pack a list of SffSprite into one or more RGBA atlas pages.

    Fills rows up to ``max_width``, stacks rows downward, and starts a new page
    once the next row would exceed ``max_height``. Both caps are clamped to
    :data:`MAX_TEXTURE_DIM`. Returns one :class:`PackedAtlas` per page; page 0
    is always present even when the sprite list is empty.
    """

    width_cap = min(max_width, MAX_TEXTURE_DIM)
    height_cap = min(max_height, MAX_TEXTURE_DIM)

    # Build PIL images keyed by (group, image), de-duplicating linked sprites
    # that share pixel buffers.
    items: list[tuple[str, int, int, int, int, int, int, Image.Image]] = []
    for s in sprites:
        if s.width <= 0 or s.height <= 0:
            continue
        key = f"{s.group}_{s.image}"
        img = s.to_pil(transparent_index=transparent_index)
        items.append((key, s.group, s.image, s.x_axis, s.y_axis, s.width, s.height, img))

    # Sort by height desc, then width desc for shelf packing.
    indexed = list(enumerate(items))
    indexed.sort(key=lambda it: (-it[1][6], -it[1][5]))

    placements: dict[int, tuple[int, int, int]] = {}  # orig_idx -> (page, x, y)
    page_extent: dict[int, tuple[int, int]] = {}       # page -> (used_w, used_h)

    def _note_extent(page: int, x2: int, y2: int) -> None:
        cur = page_extent.get(page, (0, 0))
        page_extent[page] = (max(cur[0], x2), max(cur[1], y2))

    page = 0
    cur_x = padding
    cur_y = padding
    row_h = 0

    for orig_idx, (_, _, _, _, _, w, h, _) in indexed:
        # Wrap to a new row when the sprite no longer fits in the current one.
        if cur_x + w + padding > width_cap:
            cur_x = padding
            cur_y += row_h + padding
            row_h = 0
        # Spill to a new page when the current row would exceed the height cap.
        # The guard prevents an endless page cascade for a lone oversized sprite.
        if cur_y + h + padding > height_cap and (cur_x > padding or cur_y > padding):
            page += 1
            cur_x = padding
            cur_y = padding
            row_h = 0
        placements[orig_idx] = (page, cur_x, cur_y)
        _note_extent(page, cur_x + w + padding, cur_y + h + padding)
        cur_x += w + padding
        if h > row_h:
            row_h = h

    num_pages = page + 1
    page_images: list[Image.Image] = []
    page_dims: list[tuple[int, int]] = []
    for p in range(num_pages):
        used_w, used_h = page_extent.get(p, (padding, padding))
        atlas_w = min(_next_pow2(used_w), MAX_TEXTURE_DIM)
        atlas_h = min(_next_pow2(used_h), MAX_TEXTURE_DIM)
        page_images.append(Image.new("RGBA", (atlas_w, atlas_h), (0, 0, 0, 0)))
        page_dims.append((atlas_w, atlas_h))

    page_sprites: list[list[PackedSprite]] = [[] for _ in range(num_pages)]
    for orig_idx, (key, group, image, ax, ay, w, h, img) in enumerate(items):
        p, x, y = placements[orig_idx]
        page_images[p].paste(img, (x, y))
        page_sprites[p].append(PackedSprite(
            key=key, group=group, image=image,
            x=x, y=y, w=w, h=h,
            pivot_x=ax, pivot_y=ay, page=p,
        ))

    return [
        PackedAtlas(width=page_dims[p][0], height=page_dims[p][1],
                    sprites=page_sprites[p], image=page_images[p])
        for p in range(num_pages)
    ]


def _page_file(basename: str, page: int) -> str:
    """Filename for a given atlas page (page 0 keeps the plain basename)."""
    return f"{basename}.png" if page == 0 else f"{basename}_{page}.png"


def save_atlas(pages: list[PackedAtlas], out_dir: str, basename: str = "atlas") -> tuple[list[str], str]:
    """Write every atlas page PNG plus a combined ``<basename>.sprites.json``.

    Returns ``(png_paths, json_path)``. The JSON carries a top-level ``pages``
    list (index/width/height/file) and every sprite records its ``page``. The
    legacy ``atlas`` key mirrors page 0 for backward compatibility.
    """
    os.makedirs(out_dir, exist_ok=True)
    json_path = os.path.join(out_dir, f"{basename}.sprites.json")

    png_paths: list[str] = []
    pages_meta: list[dict] = []
    sprites_payload: list[dict] = []
    for page_index, atlas in enumerate(pages):
        if atlas.image is None:
            raise ValueError(f"Atlas page {page_index} has no image to save")
        file_name = _page_file(basename, page_index)
        png_path = os.path.join(out_dir, file_name)
        atlas.image.save(png_path, "PNG")
        png_paths.append(png_path)
        pages_meta.append({
            "index": page_index,
            "width": atlas.width,
            "height": atlas.height,
            "file": file_name,
        })
        for s in atlas.sprites:
            sprites_payload.append({
                "key": s.key,
                "group": s.group,
                "image": s.image,
                "x": s.x, "y": s.y, "w": s.w, "h": s.h,
                "pivotX": s.pivot_x, "pivotY": s.pivot_y,
                "page": s.page,
            })

    payload = {
        "atlas": pages_meta[0] if pages_meta else {"width": 0, "height": 0, "file": f"{basename}.png"},
        "pages": pages_meta,
        "sprites": sprites_payload,
    }
    with open(json_path, "w", encoding="utf-8") as fh:
        json.dump(payload, fh, indent=2)

    return png_paths, json_path


def _next_pow2(n: int) -> int:
    p = 1
    while p < n:
        p <<= 1
    return p
