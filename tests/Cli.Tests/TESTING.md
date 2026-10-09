# Testing the .NET orchestrator

```powershell
dotnet build VoiceLocalCli.slnx -c Release
dotnet test VoiceLocalCli.slnx -c Release --no-build `
  --coverage --coverage-output-format cobertura `
  --coverage-output coverage.cobertura.xml `
  --coverage-settings tests/Cli.Tests/coverage.config
```

Produces `TestResults/coverage.cobertura.xml` with a single, merged `line-rate` across both
test projects. The 85% gate itself is enforced by `.github/workflows/ci.yml`'s own step
(reads that `line-rate`), not by this command -- there is no coverlet-style `/p:Threshold`
on this collector.

## Why `Microsoft.Testing.Extensions.CodeCoverage`, not `coverlet.msbuild`

Both test projects use xUnit v3 on Microsoft.Testing.Platform (MTP), not classic VSTest.
`coverlet.msbuild`'s `/p:CollectCoverage=true` hooks into VSTest's data-collector pipeline
-- against an MTP-based project it silently breaks test discovery (`dotnet test` reports
"Zero tests ran", exit code 5, with no message pointing at the real cause).
`Microsoft.Testing.Extensions.CodeCoverage` is Microsoft's own MTP-native collector, invoked
through `dotnet test`'s built-in `--coverage` flag.

## Why `--coverage-output` is the same literal filename for the whole solution

Running `dotnet test VoiceLocalCli.slnx` executes both test projects in one invocation.
Giving each project its own output filename (or no `--coverage-output` at all) produces two
separate per-project `.cobertura.xml` files under `TestResults/`, since each test host writes
its own report independently. Passing one shared `--coverage-output` filename instead makes
the collector merge both runs' line hits into a single report directly, without a separate
merge step.

## `coverage.config`'s module exclusions, and the XML gotcha hiding in it

`tests/Cli.Tests/coverage.config` excludes `VoiceLocalCli.Infrastructure.dll` and
`VoiceLocalCli.dll` (the `Ui.Console` project's assembly name) from the percentage -- real
Win32/process glue and the Spectre.Console composition root, both deliberately tested
against fakes (`IProcessLauncher`/`IChildProcess`) rather than exercised directly. This
mirrors the boundary `src/engine/TESTING.md` draws for the Python side's real hardware paths;
see `docs/MANUAL_VERIFICATION.md` for what a human still has to check there by hand.

A literal double hyphen (`--`) inside an XML comment anywhere in that settings file is
invalid XML per the XML spec, and the coverage collector fails to parse the whole file
without printing an error that names the file or the reason -- it just reports zero tests
ran. Keep that file's comments hyphen-free.

## What's in scope for the 85% gate

`VoiceLocalCli.Domain` and `VoiceLocalCli.Application` -- the hexagonal core: use cases,
ports, the `@@STATE:` line classifier, argument builders. Covered by
`VoiceLocalCli.Application.Tests` against hand-rolled fakes
(`FakeProcessLauncher`/`FakeChildProcess`), plus `VoiceLocalCli.Architecture.Tests` enforcing
the dependency rule (contributes no line coverage of its own by design -- it reflects on
types, it doesn't execute application logic).

## What's deliberately out of scope, and why

- **`VoiceLocalCli.Infrastructure`** (`RealChildProcess`, `RealProcessLauncher`,
  `ConsoleCtrlHandler`, `ProcessJobObject`): real `System.Diagnostics.Process` calls and
  Win32 P/Invoke. No hosted CI runner gives you a real `ffmpeg` child process with a
  redirected stdin behaving like the real one, or a real console-close event to catch. The
  ffmpeg stdin-`'q'` stop mechanism and the Job Object orphan-prevention backstop were both
  spiked manually against the real installed build instead -- see
  `docs/MANUAL_VERIFICATION.md`.
- **`VoiceLocalCli.Ui.Console`** (`DictationView`, `Program.cs`'s composition root): rendering
  and wiring, not logic -- `docs/MANUAL_VERIFICATION.md` item 5 already flags the color theme
  as something a human has to look at in a real terminal, not something a unit test can judge.

If a future change makes either of these newly testable at reasonable cost, move it into
scope and update this file and `coverage.config` together -- don't let the gate silently
grow or shrink without a stated reason here.
