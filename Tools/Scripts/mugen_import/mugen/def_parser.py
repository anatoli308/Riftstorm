"""MUGEN .def file parser.

The .def is an INI-like file with sections in square brackets and `key = value`
pairs. Comments start with ';'. Values may have trailing comments.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field

from .air import _read_text  # reuse robust decoder


@dataclass
class CharDef:
    """Subset of the .def file we actually care about for the importer."""

    name: str = ""
    display_name: str = ""
    author: str = ""
    sprite_file: str = ""        # SFF
    anim_file: str = ""          # AIR
    cmd_file: str = ""
    snd_file: str = ""
    cns_file: str = ""           # main CNS (constants)
    state_files: list[str] = field(default_factory=list)  # St, St1..St9
    common_state_file: str = ""  # StCommon (defaults to common1.cns)
    palettes: dict[int, str] = field(default_factory=dict)  # slot -> rel path
    raw: dict[str, dict[str, str]] = field(default_factory=dict)


_SECTION_RE = re.compile(r"^\s*\[([^\]]+)\]\s*$")
_KV_RE = re.compile(r"^\s*([^=;]+?)\s*=\s*(.*?)\s*$")


def parse_def(path: str) -> CharDef:
    text = _read_text(path)
    sections: dict[str, dict[str, str]] = {}
    current: str = ""

    for raw_line in text.splitlines():
        # Strip inline comments but keep quoted strings intact (good enough
        # for MUGEN files — they don't use `;` inside values).
        line = raw_line.split(";", 1)[0].rstrip()
        if not line.strip():
            continue

        m_section = _SECTION_RE.match(line)
        if m_section:
            current = m_section.group(1).strip().lower()
            sections.setdefault(current, {})
            continue

        m_kv = _KV_RE.match(line)
        if m_kv and current:
            key = m_kv.group(1).strip().lower()
            value = m_kv.group(2).strip().strip('"')
            sections[current][key] = value

    info = sections.get("info", {})
    files = sections.get("files", {})

    cd = CharDef(
        name=info.get("name", ""),
        display_name=info.get("displayname", info.get("name", "")),
        author=info.get("author", ""),
        sprite_file=files.get("sprite", ""),
        anim_file=files.get("anim", ""),
        cmd_file=files.get("cmd", ""),
        snd_file=files.get("sound", ""),
        cns_file=files.get("cns", ""),
        common_state_file=files.get("stcommon", ""),
        raw=sections,
    )

    # State files: St, St1..St9
    if "st" in files and files["st"]:
        cd.state_files.append(files["st"])
    for i in range(1, 10):
        key = f"st{i}"
        if key in files and files[key]:
            cd.state_files.append(files[key])

    # Palettes: Pal1 .. Pal12 (sparse slots allowed)
    for i in range(1, 13):
        key = f"pal{i}"
        if key in files and files[key]:
            cd.palettes[i] = files[key]

    return cd
