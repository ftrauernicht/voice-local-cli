# ADR-0005: The installed app bundles and bootstraps the Python engine itself

- **Status**: Accepted
- **Date**: 2026-10-09

## Context

The first real Velopack-packaged release (v0.1.0) crashed immediately on install:

```
Could not find 'VoiceLocalCli.slnx' above C:\Users\...\AppData\Local\VoiceLocalCli\current\
```

ADR-0004 already named this as a known, deliberately out-of-scope gap: Velopack only ever
updates the .NET orchestrator (`src/Cli/`), never the Python engine (`src/engine/`) or the
PowerShell scripts (`scripts/`) those paths depend on. `RepositoryLayout` only knew how to
walk up from the exe looking for `VoiceLocalCli.slnx`, which exists only inside a git
checkout -- an installed copy has no checkout anywhere near it.

Asked directly how to fix it: "Das Setup soll ALLES ausliefern was notwendig ist.
Alternativ direkt herunterladen von Github bei der Installation - Updates sollen diese
Dateien ebenfalls berücksichtigen können und zur Verfügung stellen bzw. Updates." -- the
installed app should be self-sufficient, and updates should keep these files current too.

## Decision

**Bundle the engine's source and the scripts inside the same Velopack package, don't
fetch them from GitHub at install time.** The engine's source (`src/engine/*.py` +
`pyproject.toml`) and the scripts (`scripts/*.ps1`) are tens of KB total -- what's
actually heavy (`torch`, `transformers`, `pyannote.audio`, ...) has to be `pip install`ed
on the target machine regardless, for platform/CPU/GPU-specific wheels, exactly like
`scripts/Setup.ps1` already does for a source checkout. A live GitHub download at install
time would add a network dependency and GitHub API/rate-limit surface for no benefit over
just shipping the same tiny files in the same artifact already being downloaded.

Concretely:

- `release.yml` copies `src/engine/*.py` + `pyproject.toml` and `scripts/*.ps1`, plus the
  per-mode shortcut icons, into the published `publish/` folder before `vpk pack`s it --
  they ship as direct siblings of the exe inside the same package Velopack installs and
  updates. Updating the app updates these for free; no bespoke downloader needed.
- `RepositoryLayout.FindEngineRoot`/`FindScriptsRoot` try the dev-checkout `.slnx` walk
  first (unchanged dev-mode behavior), then fall back to these bundled folders next to the
  exe. Both throw only if neither exists.
- **The Python venv itself does NOT live inside the bundled `engine` folder.** Velopack
  replaces the entire `current\` directory's contents on every update; a venv living
  inside it would need a full multi-hundred-MB `pip install` rebuild on every single app
  update. Installed-mode venv location: `%LOCALAPPDATA%\voice-local-cli\engine\.venv\` --
  stable, outside Velopack's update-managed folder, rebuilt only when actually needed.
- `scripts/Setup.ps1` gained two optional parameters, `-EngineDir`/`-VenvDir`, both
  defaulting to today's hardcoded relative paths -- **zero behavior change for existing
  dev usage**, which already runs it with no arguments. The installed-mode bootstrap calls
  it with explicit paths instead of reimplementing its ffmpeg/Python/pip logic.
- When an installed copy's venv or ffmpeg is missing, it offers (asks first, via
  `AnsiConsole.Confirm` -- never silent) to run the bundled `Setup.ps1` right there,
  interactively (inherited console, real winget/pip output visible). A dev checkout
  without a built venv keeps today's "run scripts\Setup.ps1 yourself" message unchanged --
  it has no other way to reach Setup.ps1, a dev checkout always does.
- After a successful bootstrap, the venv is stamped with the orchestrator's own version
  (`RepositoryLayout.StampVenvVersion`). If a later update ships a newer version than the
  stamp, the Python engine's environment-check row stays `Ok` (the venv still works) but
  notes the mismatch, and a new Settings item, "Repair engine setup," re-runs the
  bootstrap on demand. Deliberately not automatic: a dependency change severe enough to
  need a venv rebuild should be the operator's call, not a surprise multi-minute,
  multi-hundred-MB download triggered by an unrelated app update.

## Consequences

- Desktop shortcuts ("Create desktop shortcuts" in Settings) needed the same fix for the
  same reason: they always targeted `dotnet run --project ...`, which only works from a
  checkout with the .NET SDK installed. They now branch on `IUpdateChecker.IsInstalled`:
  dev mode keeps the `dotnet run` target, installed mode targets the installed exe
  directly via `Velopack.Locators.VelopackLocator.Current` (`AppContentDir` +
  `ThisExeRelativePath`), no `dotnet`/project path involved.
- `OrchestratorSettings`'s recordings-folder default also needed a real installed-mode
  answer -- there is no checkout to put a `recordings\` folder in. Default is
  `%LOCALAPPDATA%\voice-local-cli\recordings\`, still overridable via the existing
  Settings menu item.
- Confirmed for real against the actual v0.1.0 install on this machine (binaries and the
  bundled `engine`/`scripts`/`Assets` folders updated in place, matching what a real
  `vpk`-packed release now produces): the app no longer crashes, correctly detects
  `IsInstalled = true`, resolves the venv to the new stable LOCALAPPDATA path (not the
  bundled folder), and reaches the bootstrap offer. The interactive confirmation itself
  (`AnsiConsole.Confirm`) and the full winget/pip run it would trigger still need a human
  at a real keyboard -- see `docs/MANUAL_VERIFICATION.md`, same class of limitation as
  every other interactive prompt in this app.
