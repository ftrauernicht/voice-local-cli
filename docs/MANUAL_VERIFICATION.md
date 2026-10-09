# Manual verification: VoiceLocalCli.Ui.Console

This orchestrator has not been run against a real microphone, a real window, or a real
desktop with its full interactive flow -- doing that would mean controlling the
operator's real desktop and typing into whatever window happens to have focus, which is
out of bounds for an unsupervised coding session. That caution does not extend to simply
launching the exe and watching it start, though: the WP3 build skipped even that, on the
reasoning above, and as a direct result shipped a crash-on-startup bug (see below) that
`dotnet build`/`dotnet test` had no way to catch, since nothing exercises `Program.cs`'s
top-level statements. Found only once the app was actually run, by hand, on 2026-10-08.
Launching the app non-interactively to confirm it doesn't crash immediately is cheap,
doesn't touch anything outside the terminal session running it, and should always be
done -- it's the full interactive session (real mic, real typing into focus) that needs
a human.

## What was verified for real

- **`dotnet build VoiceLocalCli.slnx`** and **`dotnet test --solution VoiceLocalCli.slnx`**:
  all src/ projects and both test projects build clean (0 warnings, 0 errors under
  `TreatWarningsAsErrors`), 52 tests pass (99%+ line coverage on the hexagonal core).
- **`dotnet format VoiceLocalCli.slnx --verify-no-changes`**: clean, the format gate would pass.
- **`dotnet run --project src\Cli\VoiceLocalCli.Ui.Console`, run directly on 2026-10-08**:
  caught two real crashes, both described in `docs/UI_THEME.md` ("Two real bugs this
  theme already caused") -- `Color.FromInt32(0x58A6FF)` throwing on the first line of
  `Main`, and the header markup interpolating a `Color`'s `ToString()` (`"(RGB=...)"`,
  not valid markup) instead of a hex string, which crashed Live and Dictate's live view
  the instant a real phase change arrived. Both fixed. The interactive
  `SelectionPrompt`/`TextPrompt` menu itself still can't be exercised from a
  non-interactive tool session (Spectre throws `NotSupportedException`/
  `InvalidOperationException` when it can't read real console input) -- that part still
  needs a human at a real keyboard, see below.
- **All three modes, end to end, with a real microphone, `--mode` bypassing the menu**:
  using a temporary env-var test seam (added, exercised, then fully reverted before
  committing -- never shipped) to bypass only the two things a non-interactive tool
  session structurally cannot do (the `SelectionPrompt`/`TextPrompt` menu, and
  `AnsiConsole.Live`/`Status`'s need for a real console cursor), each mode was run
  against the real `Headset Microphone (2- INZONE H9 II - Chat)` device for several
  seconds and confirmed to, for real:
  - **Dictate**: launch ffmpeg + dictate.py, run silently (no speech spoken, so
    `pyautogui.write` deliberately never fired -- this did not test real typing into a
    focused window, which still needs a human, see item 1 below), and exit 0 cleanly.
  - **Live**: launch ffmpeg (continuous capture, not segmented) + transcribe_live.py,
    produce a real 16kHz/mono WAV file Python's `wave` module opens cleanly (a correctly
    finalized header, not a hard-kill truncation), and exit 0 after the graceful stop.
  - **Call**: launch ffmpeg, stop it cleanly, correctly detect no `HF_TOKEN` and warn
    instead of failing, launch transcribe.py against the finished recording, relay its
    `transcribing_full` → `finished` phase events, and produce the final `.txt` output
    at exactly the path `StopAndTranscribeAsync` returned.
  - **Crash recovery**: when the app crashed (the markup bug, before it was fixed) partway
    through a Live session with ffmpeg and the engine both running, `Get-Process
    ffmpeg,python` afterward showed neither still running -- the Job Object backstop tore
    down the whole tree on an actual unhandled-exception crash, not just the earlier
    synthetic spike.

  `--mode dictate`/`live`/`call` (bypassing the menu, what the desktop shortcuts use) was
  also confirmed to correctly detect a missing Python venv or missing ffmpeg and fail with
  a clear, actionable message instead of crashing, before `scripts\Setup.ps1` had been run
  in this checkout.
- **The ffmpeg stdin-'q' stop mechanism**: spiked directly against the installed
  `Gyan.FFmpeg` build (the same one `scripts/Setup.ps1` installs) with a throwaway console
  app that launched `ffmpeg -f lavfi -i anullsrc=r=16000:cl=mono ...` (a synthetic silent
  source -- no real microphone involved), wrote `'q'` to its redirected stdin after 2
  seconds, and confirmed:
  - stderr showed ffmpeg's own `[q] command received. Exiting.` message,
  - the process exited with code 0 within the 5-second timeout (no kill-tree fallback needed),
  - the output WAV's RIFF header size field exactly matched the file size minus 8 bytes --
    a correctly finalized header, not a hard-kill truncation.

  This directly confirms `RealChildProcess`/`DictationSession.StopAsync`'s primary path
  works as designed against the real ffmpeg build this project uses, not just against
  documentation of ffmpeg's general Windows console-input behavior.

## What still needs a human running the real executable

1. **Run Dictate end to end with real speech.** `dotnet run --project
   src\Cli\VoiceLocalCli.Ui.Console`, pick "Dictate", enter the real microphone device
   name (from `Set-AudioDevices.ps1`'s output against `scripts/Setup.ps1`'s installed
   ffmpeg), speak, and confirm:
   - the header shows `Listening` → `Transcribing` → `Typing` transitioning live,
   - the scrolling transcript panel shows recognized text,
   - the text actually types into whatever window had focus when you started speaking.

   (The process plumbing underneath this -- launching ffmpeg/the engine, producing a
   real audio file, stopping cleanly -- is already confirmed for real, see above. What's
   left here is specifically the live view's visual correctness and real typing, neither
   of which an unsupervised session should exercise itself.)
2. **Press Esc and confirm a clean stop, in all three modes.** The log/transcript file
   should close properly; open Task Manager (or `Get-Process
   ffmpeg,python -ErrorAction SilentlyContinue` in PowerShell) and confirm neither
   `ffmpeg.exe` nor `python.exe` is still running. For Call mode specifically, also watch
   the "Transcribing..."/"Identifying speakers..." status spinner actually update as the
   phases change.
3. **Close the console window via the X button instead of Esc**, and repeat the Task
   Manager check. This is the specific bug this whole redesign exists to fix (an earlier
   PowerShell-based version left `ffmpeg` recording in the background for 35+ minutes
   after a window close) -- confirm the Job Object (`ProcessJobObject`) actually tears
   both children down. If this fails, `ConsoleCtrlHandler`'s `CTRL_CLOSE_EVENT` path and
   the Job Object fallback both need re-examining; don't assume the fix works from the
   code alone.
4. **Kill the orchestrator hard** (`Stop-Process` or Task Manager "End Task") while a
   session is running, and check again for orphaned `ffmpeg.exe`/`python.exe` processes.
   This specifically exercises the Job Object path alone (`ConsoleCtrlHandler` never runs
   on a hard kill).
5. **The GitHub-Dark-inspired color theme** (`DictationView`'s `AccentStyle`/etc.) is a
   judgment call on an underspecified "loosely GitHub-inspired" request -- look at it in a
   real terminal (Windows Terminal, and separately legacy `conhost` if that matters to
   you) and decide if it needs adjusting. The values are centralized in one place for
   exactly this reason.
6. **The hotword-review `input()` prompt** (dictate.py's end-of-session interactive
   question) is NOT currently bridged into the Spectre UI -- it still reads/writes the
   engine's own stdin/stdout directly, invisible to the orchestrator. If this matters for
   v1, see the "judgment calls" note in src/Cli's design: either redirect the engine's
   stdin too and surface the prompt as a Spectre `TextPrompt`, or explicitly suppress it
   for orchestrated runs. Deferred, not forgotten.
7. **Live and Call modes are now wired up** (2026-10-08, at Frank's explicit request to
   get all three working, not just Dictate) -- `LiveTranscriptSession`/
   `CallRecordingSession`, their own argument builders, and `LiveTranscriptView`/the
   Call-mode recording+transcribing views. End-to-end process/file-level behavior is
   confirmed for real (see above); still needs a human for the same reason item 1 does:
   watching the live view render correctly and, for Call mode, trying it with a real
   `HF_TOKEN` set (`Save-HfToken.ps1`) to confirm diarization actually runs and produces
   speaker labels, not just the no-token fallback path this session could safely test.
8. **"Create desktop shortcuts"** (the menu's fourth option) was verified mechanically --
   `DesktopShortcutPlanTests`/`CreateDesktopShortcutsUseCaseTests` cover the planning logic
   against a fake writer, and `RealShortcutWriter`'s `WScript.Shell` COM calls build and
   run without throwing -- but the resulting `.lnk` files have not been double-clicked for
   real. Run the menu option, then from the real Desktop:
   - confirm all three shortcuts appear with their distinct per-mode icons
     (`src/Cli/VoiceLocalCli.Ui.Console/Assets/icons/*.ico` -- placeholder art, not yet
     reviewed against the project's eventual brand direction in `docs/branding/`),
   - double-click each shortcut and confirm it launches the orchestrator straight into
     its own mode (no menu prompt), from this repository as its working directory --
     all three are real sessions now (see item 7), not a "coming soon" placeholder.
9. **The Velopack release pipeline (`.github/workflows/release.yml`) has never run.**
   `vpk pack`/`vpk upload github`'s flags were confirmed against the real, installed
   `vpk` CLI's own `--help` output (not guessed or taken from docs that might be stale),
   and `VelopackApp.Build().Run()`/the startup update check were confirmed not to break
   a normal `dotnet run` launch (`IsInstalled` correctly reports false, the check is
   skipped, no exception, no delay). What's still unconfirmed because no tag has ever
   been pushed and this repo isn't on GitHub yet:
   - that `release.yml` actually succeeds on a real `windows-latest` runner,
   - that the resulting GitHub Release's assets are actually consumable by
     `GithubSource`/`UpdateManager.CheckForUpdatesAsync()` from a real installed copy,
   - the full update flow end to end: install v1 for real (`vpk`'s own installer, not
     `dotnet run`), publish v2, confirm the app notices it, confirm "Update to vX" in
     Settings downloads and restarts into the new version cleanly.
   First tag, first release, and this whole checklist are all blocked on the repo
   existing on GitHub -- nothing here can be verified further until then.
