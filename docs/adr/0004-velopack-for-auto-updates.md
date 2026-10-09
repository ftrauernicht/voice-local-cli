# ADR-0004: Velopack for keeping the installed app up to date

- **Status**: Accepted
- **Date**: 2026-10-09

## Context

The orchestrator is meant to be installed once and used daily, not rebuilt from source
on every change. Without some update mechanism, picking up a new feature or fix means
manually noticing a change happened, pulling the repo, and rebuilding -- real friction
for a tool whose whole point is reducing friction elsewhere.

Three real options were researched:

- **winget**: structurally doesn't fit a private repo today. A winget source needs
  either a public `winget-pkgs` manifest (which would force the repo public before it's
  ready) or a self-hosted package index -- infrastructure this project doesn't have and
  doesn't need yet.
- **MSIX**: needs app identity and a code-signing certificate, disproportionate for a
  single-maintainer tool with no code-signing setup today.
- **Velopack** (the actively maintained successor to Squirrel.Windows/Squirrel.Mac): has
  GitHub Releases as a built-in update source, and specifically solves the hard
  Windows-specific problem of a running executable being unable to overwrite itself
  (its own installer stub handles the swap-and-relaunch).

A bespoke "poll the GitHub API, download the new exe, replace it, relaunch" updater was
also considered and rejected: Windows file-locking means the replace step alone needs a
separate helper process, which is exactly the problem Velopack already solves. Building
and maintaining that by hand would be reinventing Velopack, worse.

## Decision

We will use Velopack, with GitHub Releases as the update source, with these specific
choices:

- `VelopackApp.Build().Run()` runs as the very first statement in `Program.cs`, before
  anything else -- required so Velopack's own install/update/uninstall hook invocations
  are handled and exit immediately, never falling through into the app's normal flow.
- Update checking happens once at startup, with a short (3-second) timeout, and is
  **never fatal and never automatic** -- no network, no GitHub release yet, or a slow
  response all result in silently finding nothing, and finding an update only ever shows
  a notice; applying one is a separate, explicit action in the Settings menu. An update
  must never interrupt a running dictation session or apply itself without being asked.
- `IUpdateChecker.IsInstalled` gates the whole mechanism: running from source
  (`dotnet run`, the common case during development) is not a Velopack-managed install,
  so the check is skipped entirely rather than attempting anything against a
  nonexistent install.
- Releases are built by `.github/workflows/release.yml`, triggered by pushing a `vX.Y.Z`
  tag: `dotnet publish` a self-contained single-file `win-x64` build, `vpk pack` it, then
  `vpk upload github --publish`.

## Consequences

- No release has ever been published (no tag exists, and this repo isn't pushed to
  GitHub yet) -- `release.yml` is written against the real, verified `vpk` CLI's
  documented flags, but has not itself been run for real. See
  `docs/MANUAL_VERIFICATION.md` for what that specifically means is still unconfirmed.
- This only updates the .NET orchestrator (`src/Cli/`), not the Python engine
  (`src/engine/`). As long as distribution stays "clone/pull the repo, run
  `scripts/Setup.ps1`," a `git pull` already picks up engine changes independent of
  Velopack. This becomes a real gap only if a packaged exe is ever distributed without
  an accompanying git checkout -- deliberately out of scope for this first pass, not
  solved by it.
- `scripts/Setup.ps1` stays exactly what it is (fresh-machine bootstrap: ffmpeg, the
  Python venv, optional GPU extras) -- it is a different problem from "keep an existing
  install current," and this decision does not fold one into the other.
