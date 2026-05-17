"""MUGEN character importer for Unity.

Pipeline:
    .def  -> file references + char metadata
    .sff  -> PNG sprites (per group/image, with shared palette)
    .air  -> JSON animation list (frame timing, offsets, flip flags, blend)
    PNGs  -> packed atlas + 8-direction layout (mirror-based fallback)

Modules are intentionally small and focused (see project rule: "many small
files > few large files").
"""

__all__ = [
    "sff_v1",
    "air",
    "def_parser",
    "atlas",
    "eight_dir",
    "palette",
    "cns_parser",
    "cns_states",
    "cmd_parser",
    "snd_parser",
]
