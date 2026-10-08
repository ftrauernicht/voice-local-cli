# ADR-0001: Extract the voice engine into its own standalone, public-facing repository

- **Status**: Accepted
- **Date**: 2026-10-08

## Context

The speech engine behind this project started as `meeting-transcription`, an internal
tool inside IT Titans' `workstation-tools` monorepo, built to record and transcribe
client calls with speaker diarization. Using its Dictate mode daily for voice input into
an AI coding assistant's chat changed what the tool is actually for: the real value is
hands-free local dictation to control your own tools, not meeting transcription. Call and
Live mode are a secondary, interesting-but-not-primary capability of the same underlying
speech models.

A tool built around that primary use case is worth sharing as a standalone project --
something other developers who want local, offline voice input for AI coding assistants
could use directly, independent of IT Titans' internal tooling, client work, or naming
conventions.

## Decision

We will extract the speech engine and its PowerShell bootstrap scripts into a new,
standalone repository (`voice-local-cli`, this one), under the owner's personal GitHub
account rather than an IT Titans repository, with:

- A fresh git history (`git init`, no history imported from `workstation-tools`): the
  extraction involves real restructuring (a new `src/engine`/`src/Cli`/`scripts/` layout,
  Dictate reframed as primary rather than secondary), not a verbatim move, so there is
  little to gain from fighting to preserve the old history.
- All code, comments, docs, and commits in English, unlike the rest of the owner's
  German-language work -- this is a public-facing project.
- MIT licensing for the repository's own code, with third-party speech models tracked
  separately (see NOTICE and ADR-0002's sibling concerns around licensing care).
- A quality bar matching the owner's existing `dotnet-architecture-hexagonal-template`:
  hexagonal architecture where applicable, SHA-pinned CI/CD, diff-aware coverage gates,
  ADRs, Conventional Commits.

## Consequences

- The project can be shared, starred, and used by people with no relationship to IT
  Titans or its clients -- that was never realistically true for `meeting-transcription`
  living inside an internal monorepo.
- Losing the old git history means losing blame/history context for the carried-over
  Python files' earlier evolution; acceptable because the restructuring would have
  obscured most of that history anyway.
- Two codebases now exist with a shared ancestor (the internal `meeting-transcription`
  tool and this repo) that can drift independently. No mechanism syncs them; a fix made
  in one has to be deliberately ported to the other if still relevant.
