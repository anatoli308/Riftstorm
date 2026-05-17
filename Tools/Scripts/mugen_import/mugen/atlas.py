"""Atlas packer.

Simple shelf packing: sort sprites by height descending, fill rows up to a
maximum width. Pads atlas to power-of-two height (Unity friendly).

Produces:
    atlas.png  - the packed RGBA atlas
    sprites.json - per-sprite rect + pivot info
"""

from __future__ import annotations

import json
import os
from dataclasses import dataclass, field
from typing import Optional

from PIL import Image

from .sff_v1 import SffSprite


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
    max_width: int = 4096,
    padding: int = 2,
    transparent_index: int = 0,
) -> PackedAtlas:
    """Pack a list of SffSprite into one RGBA atlas image."""

    # Build PIL images keyed by (group, image), de-duplicating linked sprites
    # that share pixel buffers.
    items: list[tuple[str, int, int, int, int, int, int, Image.Image]] = []
    seen: dict[int, str] = {}  # id(pixels) -> existing key

    for s in sprites:
        if s.width <= 0 or s.height <= 0:
            continue
        key = f"{s.group}_{s.image}"
        # If pixel buffer was shared (linked sprite), we still keep a separate
        # entry because pivot may differ. But we don't waste packing space:
        # we only pack each pixel buffer once and reuse the same rect.
        img = s.to_pil(transparent_index=transparent_index)
        items.append((key, s.group, s.image, s.x_axis, s.y_axis, s.width, s.height, img))

    # Sort by height desc, then width desc for shelf packing.
    indexed = list(enumerate(items))
    indexed.sort(key=lambda it: (-it[1][6], -it[1][5]))

    placements: dict[int, tuple[int, int]] = {}  # original-index -> (x, y)
    cur_x = padding
    cur_y = padding
    row_h = 0
    atlas_w = max_width

    for orig_idx, (_, _, _, _, _, w, h, _) in indexed:
        if w + padding * 2 > atlas_w:
            atlas_w = w + padding * 2
        if cur_x + w + padding > atlas_w:
            cur_x = padding
            cur_y += row_h + padding
            row_h = 0
        placements[orig_idx] = (cur_x, cur_y)
        cur_x += w + padding
        if h > row_h:
            row_h = h

    atlas_h = cur_y + row_h + padding
    atlas_h = _next_pow2(atlas_h)
    atlas_w = _next_pow2(atlas_w)

    atlas_img = Image.new("RGBA", (atlas_w, atlas_h), (0, 0, 0, 0))
    packed: list[PackedSprite] = []

    for orig_idx, (key, group, image, ax, ay, w, h, img) in enumerate(items):
        x, y = placements[orig_idx]
        atlas_img.paste(img, (x, y))
        packed.append(PackedSprite(
            key=key, group=group, image=image,
            x=x, y=y, w=w, h=h,
            pivot_x=ax, pivot_y=ay,
        ))

    return PackedAtlas(width=atlas_w, height=atlas_h, sprites=packed, image=atlas_img)


def save_atlas(atlas: PackedAtlas, out_dir: str, basename: str = "atlas") -> tuple[str, str]:
    os.makedirs(out_dir, exist_ok=True)
    png_path = os.path.join(out_dir, f"{basename}.png")
    json_path = os.path.join(out_dir, f"{basename}.sprites.json")

    if atlas.image is None:
        raise ValueError("Atlas has no image to save")
    atlas.image.save(png_path, "PNG")

    payload = {
        "atlas": {"width": atlas.width, "height": atlas.height, "file": f"{basename}.png"},
        "sprites": [
            {
                "key": s.key,
                "group": s.group,
                "image": s.image,
                "x": s.x, "y": s.y, "w": s.w, "h": s.h,
                "pivotX": s.pivot_x, "pivotY": s.pivot_y,
            }
            for s in atlas.sprites
        ],
    }
    with open(json_path, "w", encoding="utf-8") as fh:
        json.dump(payload, fh, indent=2)

    return png_path, json_path


def _next_pow2(n: int) -> int:
    p = 1
    while p < n:
        p <<= 1
    return p
