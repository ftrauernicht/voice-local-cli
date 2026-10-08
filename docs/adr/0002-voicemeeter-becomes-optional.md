# ADR-0002: Voicemeeter is optional infrastructure, not a setup requirement

- **Status**: Accepted
- **Date**: 2026-10-08

## Context

The original internal tool's setup script installed Voicemeeter (a virtual audio mixer)
unconditionally, because Call and Live mode need it to mix the remote party's audio into
a single recordable bus. Dictate mode -- now understood as this project's primary use
case, see ADR-0001 -- never needed Voicemeeter: it only ever records a single, plain
microphone input device.

Requiring Voicemeeter installation, a system reboot (its audio driver needs one), and
several manual routing steps in its GUI, for someone who only wants hands-free dictation
and will never touch Call or Live mode, is a real adoption barrier with no corresponding
benefit for that person.

## Decision

We will make Voicemeeter installation and configuration an explicitly optional,
clearly-separated section of `scripts/Setup.ps1` and the README's requirements table,
never a blocking step for Dictate-only setup. The setup script's checklist leads with
"Dictate mode is now ready to try" before a visually distinct "only if you also want
Call/Live mode" block.

## Consequences

- Someone who only wants dictation can go from `git clone` to a working `dotnet run`
  session without installing anything beyond ffmpeg, Python, and .NET -- no GUI mixer
  configuration, no reboot.
- Call/Live mode remains fully supported for anyone who does want it; nothing about this
  decision removes the capability, only its place in the default setup path.
- `FfmpegArguments.ForDictateCapture` (the .NET orchestrator's ffmpeg argument builder)
  only ever targets a plain `dshow` microphone device -- it has no code path that assumes
  a Voicemeeter-routed bus, which keeps this decision enforced by the orchestrator's
  actual capabilities, not just documentation.
