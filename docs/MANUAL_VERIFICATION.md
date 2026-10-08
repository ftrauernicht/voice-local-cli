# Manual verification: VoiceLocalCli.Ui.Console

This orchestrator has not been run against a real microphone, a real window, or a real
desktop by the agent that built it -- doing so would mean controlling the operator's real
desktop and typing into whatever window happens to have focus, which is out of bounds for
an unsupervised coding session. Everything below has been verified mechanically
(`dotnet build`, `dotnet test`, the ffmpeg-stdin spike described below); everything that
needs a human is listed here explicitly rather than silently assumed to work.

## What was verified for real

- **`dotnet build VoiceLocalCli.slnx`** and **`dotnet test --solution VoiceLocalCli.slnx`**:
  all four `src/` projects and both test projects build clean (0 warnings, 0 errors under
  `TreatWarningsAsErrors`), 43 tests pass.
- **`dotnet format VoiceLocalCli.slnx --verify-no-changes`**: clean, the format gate would pass.
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

1. **Run it end to end.** `dotnet run --project src\Cli\VoiceLocalCli.Ui.Console`, pick
   "Dictate", enter the real microphone device name (from `Set-AudioDevices.ps1`'s output
   against `scripts/Setup.ps1`'s installed ffmpeg), speak, and confirm:
   - the header shows `Listening` → `Transcribing` → `Typing` transitioning live,
   - the scrolling transcript panel shows recognized text,
   - the text actually types into whatever window had focus when you started speaking.
2. **Press Esc and confirm a clean stop.** The log should show the transcript file closed
   properly; open Task Manager (or `Get-Process ffmpeg,python -ErrorAction SilentlyContinue`
   in PowerShell) and confirm neither `ffmpeg.exe` nor `python.exe` is still running.
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
7. **Live and Call modes are not wired up** -- selecting them in the menu currently just
   prints "coming soon" and exits. The Domain/Application layers were designed to extend
   to them without rearchitecting (see `DictationSettings`, `EngineEvent`'s vocabulary
   already covering `recording`/`transcribing_chunk`/`transcribing_full`/
   `diarizing_speakers` from CONTRACT.md), but no orchestration logic for them exists yet.
8. **"Create desktop shortcuts"** (the menu's fourth option) was verified mechanically --
   `DesktopShortcutPlanTests`/`CreateDesktopShortcutsUseCaseTests` cover the planning logic
   against a fake writer, and `RealShortcutWriter`'s `WScript.Shell` COM calls build and
   run without throwing -- but the resulting `.lnk` files have not been double-clicked for
   real. Run the menu option, then from the real Desktop:
   - confirm all three shortcuts appear with their distinct per-mode icons
     (`src/Cli/VoiceLocalCli.Ui.Console/Assets/icons/*.ico` -- placeholder art, not yet
     reviewed against the project's eventual brand direction in `docs/branding/`),
   - double-click the Dictate shortcut and confirm it launches the orchestrator straight
     into Dictate mode (no menu prompt), from this repository as its working directory,
   - confirm the Live/Call shortcuts still show "coming soon" rather than erroring.
