"""Uniform, colored console output for every script here -- no extra package needed,
modern Windows terminals (PowerShell 7, Windows Terminal, current conhost) understand
ANSI color codes directly.

Also the home of the structured status-event channel (see `state()`) that a future
orchestrator (a .NET console app, see README "Roadmap") can depend on without having to
parse this module's human-readable, translatable prose. See CONTRACT.md for the full
event vocabulary.
"""

import json
import sys

_OK = "\033[92m"
_WARN = "\033[93m"
_ERR = "\033[91m"
_INFO = "\033[96m"
_END = "\033[0m"

STATE_PREFIX = "@@STATE:"


def info(msg: str) -> None:
    print(f"{_INFO}{msg}{_END}", file=sys.stderr)


def ok(msg: str) -> None:
    print(f"{_OK}[ok]{_END} {msg}", file=sys.stderr)


def warn(msg: str) -> None:
    print(f"{_WARN}[!]{_END} {msg}", file=sys.stderr)


def err(msg: str) -> None:
    print(f"{_ERR}[error]{_END} {msg}", file=sys.stderr)


def state(name: str, **fields: object) -> None:
    """Emits one machine-readable status-event line on stdout: `@@STATE:` followed by a
    JSON object with at least a "name" key. A consumer (today: nothing; planned: the .NET
    orchestrator) can filter stdout for lines starting with STATE_PREFIX and json.loads()
    the remainder -- a literal prefix check, not prose-parsing, so changing a human-
    readable message elsewhere never breaks this contract. See CONTRACT.md.

    Deliberately on stdout, not stderr: stdout already carries this project's other
    machine-relevant output (dictate.py's transcript lines) and a consumer reading a
    child process's output typically multiplexes one stream at a time; keeping this on
    the same stream as transcript lines means a single reader can't miss one by only
    watching the other. The STATE_PREFIX makes the two kinds of line trivially
    distinguishable regardless.
    """
    payload = {"name": name, **fields}
    print(f"{STATE_PREFIX}{json.dumps(payload, ensure_ascii=False)}", flush=True)
