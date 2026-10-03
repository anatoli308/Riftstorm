# MUGEN -> Unity Importer

Python pipeline that turns a MUGEN character (`.def` + `.sff` + `.air`)
into Unity-ready assets: one packed sprite atlas (PNG + JSON) plus an
8-direction animation manifest.

## Install

```powershell
python -m pip install -r requirements.txt
```

Pillow is the only runtime dependency.

## Run

```powershell
python mugen_import.py mugen_data/Mudpenis --out out/Mudpenis
```

Output:

```
out/Mudpenis/
  char.json              # metadata from .def
  atlas.png              # packed sprite atlas (RGBA)
  atlas.sprites.json     # per-sprite atlas rects + pivots
  animations.json        # 8-direction animations (E/NE/N/NW/W/SW/S/SE)
  sprites/<g>_<i>.png    # individual PNGs (skip with --no-individual-pngs)
```

## Convert and deploy to Riftstorm

After importing into `out/<Character>`, run the converter from this directory:

```powershell
python mugen_to_flare.py out\Taokaka
```

The CLI converts locally and then copies only runtime files:

| Destination | Files |
|---|---|
| `Assets/StreamingAssets/Custom_Characters/Taokaka/` | `Taokaka.json`, optional `Taokaka.stats.json` |
| `Assets/Art/Custom_Characters/Taokaka/` | All atlas PNG pages referenced by the manifest, `sounds/*.wav` |

Importer intermediates (`animations.json`, `atlas.sprites.json`, `states.json`,
`commands.json`, `constants.json`, palettes, individual sprites) stay in `out`.
Deployment copies rather than moves files. It overwrites corresponding runtime
files but preserves Unity `.meta` files and does not remove old or unrelated
destination files. Missing referenced atlas pages fail deployment with a nonzero
exit code instead of being silently skipped.

The project root defaults to the repository containing the script, regardless
of the current working directory. Multiple character directories are supported:

```powershell
python mugen_to_flare.py out\Taokaka out\Gallon
python mugen_to_flare.py out\Taokaka --project-root D:\AnotherUnityProject
python mugen_to_flare.py out\Taokaka --no-deploy
```

`--no-deploy` preserves the conversion-only workflow. `--atlas-name` changes the
JSON filenames, not the destination character folder. Direct calls to
`convert_character()` still only convert; `_importall.py` uses the same deployment
helper as the CLI.

Run the deployment regression tests with:

```powershell
python -m unittest discover -s tests -p test_mugen_deploy.py
```

## 8-direction logic

MUGEN characters are side-view 2D fighters and don't ship with back/front
art. We fake the missing directions:

| Direction | Source | Flip H | Fake? |
|-----------|--------|--------|-------|
| E         | original | no   | no    |
| W         | original | yes  | no    |
| NE / SE   | original | no   | no    |
| NW / SW   | original | yes  | no    |
| N / S     | original | no   | yes   |

`fake = true` in the JSON tells the Unity side that the silhouette is not
correct for that angle (no real top/bottom view exists). The runtime can
swap in a placeholder or just render a shadow.

## Format notes

- SFF v1 only (Elecbyte). v2 is not implemented.
- PCX subfiles, 8-bit indexed, RLE-encoded scanlines.
- Shared palette references (`use_same_palette = 1`) and linked subfiles
  (`length = 0`) are resolved.
- Palette index 0 is treated as transparent by default
  (`--transparent-index` to override).

## Unity side (sketch)

In Unity, read `atlas.sprites.json` to slice the texture into named sprites
(`<group>_<image>`), then read `animations.json` and build `AnimationClip`s
per action per direction. Pivots are MUGEN axis values (top-left relative,
in pixels).

## Adding more directions later

Once you have real top-down art for a character, drop it next to the MUGEN
data and extend `mugen/eight_dir.py` to point N/S/NE/NW/SE/SW at the new
sprite groups instead of aliasing E.
