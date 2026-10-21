# CLAUDE.md — guide for coding agents

Local, offline speech-to-text: hands-free dictation into whatever window has focus, plus two
secondary call-recording modes (Live, Call). Full description, setup and usage live in
[`README.md`](README.md) — read that first; this file only adds what a coding session needs
beyond it.

## Two codebases, one contract

- **`src/engine/`** — Python 3.11+. Three entry points (`dictate.py`, `transcribe_live.py`,
  `transcribe.py`), each wrapping `faster-whisper`/OpenVINO GenAI/CUDA for transcription and
  `webrtcvad` for pause detection. Emits structured `@@STATE:` events on stdout — the contract
  is frozen and documented in [`src/engine/CONTRACT.md`](src/engine/CONTRACT.md); read it
  before changing anything the orchestrator parses.
- **`src/Cli/`** — .NET 10, hexagonal (`Domain → Application`, `Infrastructure.*` implements
  the ports, `Ui.Console` is the composition root and Spectre.Console UI). Drives the Python
  engine as a child process.
- Decisions and their reasoning live in [`docs/adr/`](docs/adr/) — read the relevant ADR
  before changing anything it governs. If a change would violate one, say so and propose
  superseding it rather than working around it silently.

## Build / test

```powershell
# Python engine, from src/engine/
pip install -e ".[dev]"
ruff check .
pytest --cov --cov-report=term-missing

# .NET orchestrator, from the repo root
dotnet restore VoiceLocalCli.slnx
dotnet build VoiceLocalCli.slnx -c Release
dotnet test --solution VoiceLocalCli.slnx -c Release
dotnet run --project src\Cli\VoiceLocalCli.Ui.Console
```

Both sides are gated at 85% line coverage in CI; a PR that drops below fails. `ruff format`
and `dotnet format VoiceLocalCli.slnx` must stay clean too — `format.yml` checks both.

What CI and `dotnet build` cannot catch is listed in
[`docs/MANUAL_VERIFICATION.md`](docs/MANUAL_VERIFICATION.md) — check it before claiming
something "works", and add to it instead of silently skipping a verification that needs a
human.

## Current state (checked 2026-10-21)

Working tree is clean, `main` is up to date with `origin/main`. The NVIDIA/CUDA GPU backend
(the second optional accelerated path alongside Intel/OpenVINO) is implemented and merged —
`src/engine/_cuda_backend.py`, ADR-0006, README's "Supported optional GPU acceleration" table —
plus four follow-up fixes after the initial merge (#14), all caught by actually running the
installed app rather than by `dotnet build`/`pytest` alone: #16 surfaces the engine's stderr
during startup, #12/#10 made the GPU-acceleration setup step show real progress instead of
running silently, #8 fixed Python-3.14 detection. Nothing from that line of work is pending.

**One real open item.** No NVIDIA GPU exists on the machine this was built on, so the CUDA
path has never run against real hardware — only its detection/fallback logic is exercised by
the test suite's mocks. `docs/MANUAL_VERIFICATION.md`, item 20, spells out exactly what still
needs confirming on an actual RTX machine: that the cuBLAS/cuDNN prerequisite works once
installed by hand, that `--backend auto` actually prefers CUDA over OpenVINO when both are
present, and whether `compute_type="float16"` holds up across different NVIDIA GPU
generations. This needs Frank, on that machine — not another coding session.

## Conventions

- **Conventional Commits**, imperative, lowercase, no trailing period: `feat:`, `fix:`,
  `docs:`, `style:`, `refactor:`, `test:`, `ci:`, `chore:`, `build:` — matches every commit in
  `git log` so far.
- **No direct push to `main`.** Branch protection rejects it outright, owner included — every
  change goes through a PR (`gh pr merge --admin` is required even for self-approval, see
  [`docs/ci/branch-protection.md`](docs/ci/branch-protection.md)). CI (Python, .NET,
  PowerShell, format, secret/vulnerability scan) must be green first.
- **English throughout** — code, docs, commits, PR descriptions. This repo is public.
- Git identity for this repo comes from the `includeIf` in the global `.gitconfig`
  (`C:/Dev-Privat/.gitconfig` → `ftrauernicht@users.noreply.github.com`) — never set a local
  `user.email` here, it would silently override that.
- Don't commit without being asked; commit deliberately, on request.
