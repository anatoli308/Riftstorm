"""MUGEN .cmd command and command-state parser.

A .cmd file contains:
    [Defaults]              - default time/buffer.time for all commands
    [Remap]                 - button remap (a..z -> a..z)
    [Command]               - one or more named input sequences
        name      = "fwd"
        command   = F, F            ; symbols: B, F, D, U, DB, DF, UB, UF, a-z, ~D, /F, $D, +
        time      = 15
        buffer.time = 1
    [Statedef -1]           - command listener (runs every frame, no anim)
    [State -1, label]       - controllers conditional on `command="<name>"`

We split into two structures:
    * commands: list[CommandDef] parsed from [Command] blocks
    * state_machine: list[StateDef] parsed via cns_states (covers Statedef -1
      and any [State -1, ...] blocks).
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field

from .air import _read_text
from .cns_states import StateDef, parse_states


@dataclass
class CommandDef:
    """One [Command] block."""

    name: str
    command: str            # raw input sequence string, e.g. "~D, DF, F, x"
    time: int = 15          # default per MUGEN
    buffer_time: int = 1


@dataclass
class CmdFile:
    defaults_time: int = 15
    defaults_buffer_time: int = 1
    remap: dict[str, str] = field(default_factory=dict)
    commands: list[CommandDef] = field(default_factory=list)
    state_machine: list[StateDef] = field(default_factory=list)


_SECTION_RE = re.compile(r"^\s*\[([^\]]+)\]\s*$")
_KV_RE = re.compile(r"^\s*([^=;]+?)\s*=\s*(.*?)\s*$")


def parse_cmd(path: str) -> CmdFile:
    """Parse a .cmd file. Statedef/State blocks delegated to cns_states."""
    text = _read_text(path)

    cmd = CmdFile()
    section: str = ""
    current_command: dict[str, str] | None = None

    def flush_command() -> None:
        nonlocal current_command
        if current_command is None:
            return
        name = current_command.get("name", "").strip().strip('"')
        seq = current_command.get("command", "").strip()
        if name and seq:
            cmd.commands.append(CommandDef(
                name=name,
                command=seq,
                time=_to_int(current_command.get("time", ""), cmd.defaults_time),
                buffer_time=_to_int(
                    current_command.get("buffer.time", ""),
                    cmd.defaults_buffer_time,
                ),
            ))
        current_command = None

    for raw_line in text.splitlines():
        line = raw_line.split(";", 1)[0].rstrip()
        if not line.strip():
            continue

        m_sec = _SECTION_RE.match(line)
        if m_sec:
            flush_command()
            section = m_sec.group(1).strip().lower()
            if section == "command":
                current_command = {}
            continue

        m_kv = _KV_RE.match(line)
        if m_kv is None:
            continue
        key = m_kv.group(1).strip().lower()
        value = m_kv.group(2).strip().strip('"')

        if section == "defaults":
            if key == "command.time":
                cmd.defaults_time = _to_int(value, cmd.defaults_time)
            elif key == "command.buffer.time":
                cmd.defaults_buffer_time = _to_int(value, cmd.defaults_buffer_time)
        elif section == "remap":
            cmd.remap[key] = value
        elif section == "command" and current_command is not None:
            current_command[key] = value

    flush_command()

    # Delegate Statedef/State parsing to the generic state parser.
    cmd.state_machine = parse_states(path)
    return cmd


def commands_to_jsonable(cmd: CmdFile) -> dict:
    return {
        "defaults": {
            "time": cmd.defaults_time,
            "buffer_time": cmd.defaults_buffer_time,
        },
        "remap": cmd.remap,
        "commands": [
            {
                "name": c.name,
                "command": c.command,
                "time": c.time,
                "buffer_time": c.buffer_time,
            }
            for c in cmd.commands
        ],
    }


def _to_int(s: str, default: int) -> int:
    if not s:
        return default
    try:
        return int(float(s.strip()))
    except ValueError:
        return default
