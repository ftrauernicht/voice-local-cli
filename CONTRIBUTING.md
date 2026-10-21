# Contributing to voice-local-cli

A personal, solo-maintained project. Issues and pull requests are welcome, but it
isn't maintained on a fixed schedule, so response times vary. No contributor licence
agreement is required; contributions are accepted under the repository's [MIT
licence](LICENSE).

## Before you start

Read [`README.md`](README.md) for what the project does and how it's set up, and
[`CLAUDE.md`](CLAUDE.md) for how the two codebases fit together. Decisions and their
reasoning live in [`docs/adr/`](docs/adr/); if a change would contradict an ADR, say so
in the pull request and propose superseding it rather than working around it quietly.

## The two codebases

| Part | Path | Stack |
|---|---|---|
| Speech engine | `src/engine/` | Python 3.11+, `faster-whisper`/OpenVINO/CUDA, `webrtcvad` |
| Orchestrator | `src/Cli/` | .NET 10, hexagonal, Spectre.Console |

The orchestrator drives the engine as a child process and parses its `@@STATE:` events.
That contract is frozen and documented in [`src/engine/CONTRACT.md`](src/engine/CONTRACT.md).
Read it before changing anything the orchestrator parses.

## Build and test

```powershell
# Python engine, from src/engine/
pip install -e ".[dev]"
ruff check .
pytest --cov --cov-report=term-missing

# .NET orchestrator, from the repo root
dotnet restore VoiceLocalCli.slnx
dotnet build VoiceLocalCli.slnx -c Release
dotnet test --solution VoiceLocalCli.slnx -c Release
```

Both sides are gated at 85% line coverage in CI, and a pull request that drops below
that fails. `ruff format` and `dotnet format VoiceLocalCli.slnx` must stay clean too.
Some things CI cannot catch are listed in
[`docs/MANUAL_VERIFICATION.md`](docs/MANUAL_VERIFICATION.md): check it before claiming a
change works, and add to it rather than skipping a verification that needs a human.

## Pull requests

- **No direct push to `main`.** Branch protection rejects it, owner included. Every
  change goes through a pull request with green CI (Python, .NET, PowerShell, format,
  and the secret/vulnerability scan).
- Keep pull requests small and focused. They are easier to review and easier to revert.
- **Conventional Commits**, imperative and lowercase, no trailing period: `feat:`,
  `fix:`, `docs:`, `style:`, `refactor:`, `test:`, `ci:`, `chore:`, `build:`.
- **English throughout** for code, docs, commits, and pull request descriptions. This
  repository is public.
- Never commit secrets: credentials, API keys, tokens, or personal data.
