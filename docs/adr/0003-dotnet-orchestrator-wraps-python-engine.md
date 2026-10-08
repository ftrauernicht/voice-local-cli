# ADR-0003: The .NET orchestrator wraps the Python engine; it does not replace it

- **Status**: Accepted
- **Date**: 2026-10-08

## Context

The original PowerShell-based flow opened two raw console windows per mode (one for
`ffmpeg`, one for the Python transcription script) with no single, discoverable way to
stop a session cleanly -- confirmed as a real problem, not a hypothetical one, when a
window closed via the X button instead of the documented `q` keypress left `ffmpeg`
recording in the background, unnoticed, for over 35 minutes.

A full rewrite of the speech engine itself in C#/.NET was considered and rejected: the
Python side (`faster-whisper`, OpenVINO GenAI, `pyannote.audio`, `webrtcvad`,
`torchaudio`'s forced-alignment support) represents working, daily-used functionality
built on a Python-first ML ecosystem with no equivalent .NET library story. Rewriting it
would trade a working system for a long reimplementation project with no new capability
to show for it.

## Decision

We will build the .NET 10 + Spectre.Console application (`src/Cli/`) as a thin
orchestrator around the existing Python engine, not a replacement for it:

- The Python engine stays exactly what it is -- `dictate.py`/`transcribe_live.py`/
  `transcribe.py`, invoked as a child process via `System.Diagnostics.Process`.
- Communication is one-directional and line-based: the Python side emits structured
  `@@STATE:<json>` events on stdout (see `src/engine/CONTRACT.md`), which
  `EngineLineClassifier` parses into typed events the .NET side's Spectre.Console view
  reacts to.
- The .NET side owns process lifecycle and UI only: starting/stopping `ffmpeg` and the
  Python engine together, catching window-close and hard-kill events
  (`ConsoleCtrlHandler`, a Win32 Job Object) so neither is ever left orphaned, and
  rendering a live status view instead of two scrolling raw consoles.
- `IProcessLauncher`/`IChildProcess` abstract the real process calls behind a port, so the
  orchestration logic (when to start/stop what, how to classify engine output) is
  unit-testable against fakes without a real `ffmpeg`/Python process -- see
  `tests/Cli.Tests/TESTING.md` for exactly what that boundary covers and what still
  needs `docs/MANUAL_VERIFICATION.md`'s manual checks.

## Consequences

- Two runtimes and two dependency ecosystems now coexist in one repository (a Python
  virtualenv plus a .NET SDK), rather than one. Setup (`scripts/Setup.ps1`) and CI
  (`.github/workflows/ci.yml`) both have to provision and gate both independently.
- A change to the engine's observable behavior (new status events, changed stdout
  format) is a contract change against `src/engine/CONTRACT.md` that the .NET side has to
  track by hand -- there's no shared schema or generated client, by design: the contract
  is deliberately small and stable enough that this hasn't been a real cost yet.
- The .NET side can be fully unit-tested for orchestration logic without needing real
  audio hardware or a real Python environment in CI, which is what makes its 85%
  coverage gate (`tests/Cli.Tests/TESTING.md`) meaningful rather than padding -- the real
  OS/process glue it can't mock away (`VoiceLocalCli.Infrastructure`) is excluded from
  that gate and verified by hand instead.
