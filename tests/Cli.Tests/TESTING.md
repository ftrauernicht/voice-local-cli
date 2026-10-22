# Testing the .NET orchestrator

```powershell
dotnet build VoiceLocalCli.slnx -c Release

# All tests (pass/fail), the Architecture.Tests layering rules included:
dotnet test VoiceLocalCli.slnx -c Release --no-build

# Line coverage for the 85% gate -- from Application.Tests alone (see below for why):
dotnet test tests/Cli.Tests/VoiceLocalCli.Application.Tests/VoiceLocalCli.Application.Tests.csproj `
  -c Release --no-build `
  --coverage --coverage-output-format cobertura `
  --coverage-output coverage.cobertura.xml `
  --coverage-settings tests/Cli.Tests/coverage.config
```

The coverage command produces `TestResults/coverage.cobertura.xml`. The 85% gate itself is
enforced by `.github/workflows/ci.yml`'s own step (reads that report's `line-rate`), not by
this command -- there is no coverlet-style `/p:Threshold` on this collector.

## Why `Microsoft.Testing.Extensions.CodeCoverage`, not `coverlet.msbuild`

Both test projects use xUnit v3 on Microsoft.Testing.Platform (MTP), not classic VSTest.
`coverlet.msbuild`'s `/p:CollectCoverage=true` hooks into VSTest's data-collector pipeline
-- against an MTP-based project it silently breaks test discovery (`dotnet test` reports
"Zero tests ran", exit code 5, with no message pointing at the real cause).
`Microsoft.Testing.Extensions.CodeCoverage` is Microsoft's own MTP-native collector, invoked
through `dotnet test`'s built-in `--coverage` flag.

## Why coverage is scoped to `Application.Tests`, not the whole solution

Running `dotnet test VoiceLocalCli.slnx` with `--coverage` executes both test projects in one
invocation, and both write to the one `--coverage-output` file. That cross-assembly merge is
**not deterministic**: `Architecture.Tests` loads the `Domain` and `Application` assemblies by
reflection without executing their lines, so its own report lists every one of those lines as
valid-but-unhit. When that all-zero-hit report is the one that wins the merge, the combined
report comes back `line-rate=0` -- which failed the 85% gate on both a `main` push and a PR on
2026-10-22, while other runs of the very same commits merged the other way and read 97%+.

`Application.Tests` is the only suite that actually exercises the measured `Domain`/`Application`
code, so collecting coverage from it alone is both the true number and fully deterministic. The
denominator doesn't change either way: the `lines-valid` total is fixed by `coverage.config`'s
module excludes (below), not by which test project ran. The full solution still runs for
pass/fail in its own step, so `Architecture.Tests` keeps guarding the dependency direction.

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
