"""Zeigt die neu entstandenen Sequenzen der globalen Statedefs.

Kontrolliert, dass die Dauereffekte aus -2/-3 als eigene Eintraege ankommen und
nicht versehentlich eine Koerper-Animation mitbringen.
"""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
DEPLOY = ROOT / "Assets" / "StreamingAssets" / "Custom_Characters"


def main() -> None:
    for char in sorted(p.name for p in DEPLOY.iterdir() if p.is_dir()):
        path = DEPLOY / char / f"{char}.json"
        if not path.exists():
            continue
        flare = json.loads(path.read_text(encoding="utf-8"))
        rows = [s for s in flare["sequences"] if s["state"] < 0]
        if not rows:
            continue
        for seq in rows:
            print(f"{char:<16} state={seq['state']:<4} kat={seq['category']:<8} "
                  f"anims={len(seq['anims'])} fx={len(seq['effects'])} "
                  f"helper={len(seq['helpers'])} snd={len(seq['sounds'])} "
                  f"palfx={len(seq['palfx'])}  \"{seq['label'][:34]}\"")


if __name__ == "__main__":
    main()
