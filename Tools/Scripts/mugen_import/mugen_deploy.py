"""Copy converted MUGEN runtime files into Unity's data and asset folders."""

from __future__ import annotations

import json
import shutil
from pathlib import Path
from typing import cast


def deploy_character(
    manifest_path: Path,
    project_root: Path | None = None,
) -> tuple[int, int]:
    """Deploy runtime JSON, atlas pages and WAVs, preserving existing Unity metadata.

    Validate required sources before copying. Importer intermediates stay in the
    source directory; unrelated and obsolete destination files are not removed.
    Return the number of copied JSON files and art assets, respectively.
    """
    manifest_path = manifest_path.resolve()
    source = manifest_path.parent
    root = (
        project_root.resolve()
        if project_root is not None
        else Path(__file__).resolve().parents[3]
    )
    if not (root / "Assets").is_dir():
        raise FileNotFoundError(f"Unity project Assets directory not found: {root}")

    with manifest_path.open(encoding="utf-8") as handle:
        document: object = json.load(handle)
    if not isinstance(document, dict):
        raise ValueError(f"FLARE manifest must be an object: {manifest_path}")
    manifest = cast(dict[str, object], document)

    image_entries: object = manifest.get("images") or [manifest.get("image")]
    if not isinstance(image_entries, list) or not image_entries:
        raise ValueError(f"FLARE manifest must reference atlas images: {manifest_path}")
    pages = cast(list[object], image_entries)

    art_sources: list[Path] = []
    for page in pages:
        if (
            not isinstance(page, str)
            or not page
            or "/" in page
            or "\\" in page
            or ":" in page
            or Path(page).suffix.lower() != ".png"
        ):
            raise ValueError(f"Invalid atlas PNG filename {page!r} in {manifest_path}")
        atlas_path = source / page
        if not atlas_path.is_file():
            raise FileNotFoundError(f"Referenced atlas page not found: {atlas_path}")
        if atlas_path not in art_sources:
            art_sources.append(atlas_path)

    sound_source = source / "sounds"
    if sound_source.is_dir():
        art_sources.extend(
            sorted(
                path for path in sound_source.iterdir()
                if path.is_file() and path.suffix.lower() == ".wav"
            )
        )

    json_sources = [manifest_path]
    stats_path = manifest_path.with_name(f"{manifest_path.stem}.stats.json")
    if stats_path.is_file():
        json_sources.append(stats_path)

    stream_destination = root / "Assets" / "StreamingAssets" / "Custom_Characters" / source.name
    art_destination = root / "Assets" / "Art" / "Custom_Characters" / source.name
    copies = [
        (path, stream_destination / path.name)
        for path in json_sources
    ]
    copies.extend(
        (path, art_destination / path.relative_to(source))
        for path in art_sources
    )
    for path, destination in copies:
        if path == destination.resolve():
            raise ValueError(f"Deployment source and destination must differ: {path}")
    for path, destination in copies:
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, destination)

    return len(json_sources), len(art_sources)
