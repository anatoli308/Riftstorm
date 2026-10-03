"""Zeigt, welche BG-Schlüssel die vorhandenen Stage-Definitionen wirklich benutzen."""
from __future__ import annotations

import collections
import glob
import re

SECTION = re.compile(r"^\[(.+?)\]")


def main() -> None:
    keys: collections.Counter[str] = collections.Counter()
    types: collections.Counter[str] = collections.Counter()
    sections: collections.Counter[str] = collections.Counter()
    for path in glob.glob("mugen_stage_data/*/*.def"):
        current = ""
        for raw in open(path, encoding="latin-1", errors="ignore").read().splitlines():
            line = raw.split(";", 1)[0].strip()
            if not line:
                continue
            head = SECTION.match(line)
            if head:
                current = head.group(1).strip().lower()
                sections[re.sub(r"[\d.]+$", "", current).strip()] += 1
                continue
            if not current.startswith("bg ") or "=" not in line:
                continue
            key, value = (part.strip().lower() for part in line.split("=", 1))
            keys[key] += 1
            if key == "type":
                types[value] += 1
    print("Sektionen:", dict(sections))
    print("BG-Typen :", dict(types))
    print("BG-Keys  :", dict(keys))


if __name__ == "__main__":
    main()
