"""Uniform, colored console output for every script here -- no extra package needed,
modern Windows terminals (PowerShell 7, Windows Terminal, current conhost) understand
ANSI color codes directly."""
import sys

_OK = "\033[92m"
_WARN = "\033[93m"
_ERR = "\033[91m"
_INFO = "\033[96m"
_END = "\033[0m"


def info(msg: str) -> None:
    print(f"{_INFO}{msg}{_END}", file=sys.stderr)


def ok(msg: str) -> None:
    print(f"{_OK}[ok]{_END} {msg}", file=sys.stderr)


def warn(msg: str) -> None:
    print(f"{_WARN}[!]{_END} {msg}", file=sys.stderr)


def err(msg: str) -> None:
    print(f"{_ERR}[error]{_END} {msg}", file=sys.stderr)
