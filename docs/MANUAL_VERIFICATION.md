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
9. **The Velopack release pipeline is now confirmed against two real releases.**
   `v0.1.0` (2026-10-09) was the first `release.yml` run on a real `windows-latest`
   runner -- succeeded, but the installed `Setup.exe` crashed immediately (see item 17,
   ADR-0005). `v0.1.1` (2026-10-09, same day, after the fix and after the repo went
   public) confirmed the rest of this checklist for real:
   - `release.yml` succeeds on a real `windows-latest` runner, including the new
     engine/scripts/icons bundling step -- verified by downloading the published
     `.nupkg` and listing its contents, not just trusting a green checkmark.
   - The resulting GitHub Release's assets ARE consumable by
     `GithubSource`/`UpdateManager.CheckForUpdatesAsync()` from a real installed copy:
     the `v0.1.0` install already on this machine, pointed at the now-public repo,
     printed `Update available: v0.1.1 -- see Settings to update.` on a real launch --
     this needed the repo to be public first (unauthenticated `GithubSource` can't read
     a private repo's release assets, a constraint found and fixed by making the repo
     public on 2026-10-09, same day).

   Still not verified: actually choosing "Update to v0.1.1" in the Settings menu and
   confirming it downloads, applies, and restarts into the new version cleanly -- needs
   a human at a real keyboard to drive the `SelectionPrompt`, same class of limitation as
   every other interactive menu in this app.
10. **The Esc-to-stop fix (`ConsoleEscapeWatcher`, 2026-10-09) -- CONFIRMED WORKING**,
    live, by Frank, in the terminal it was originally broken in (screenshot evidence:
    "Dictation finished." printed and the process returned to the shell after pressing
    Esc). The bug it fixes was reported live: Esc did nothing while dictating, with the
    orchestrator's own console window actually focused -- ruling out the "wrong window
    has focus" explanation the original `System.Console.KeyAvailable`-based
    implementation's own try/catch assumed. The fix opens `CONIN$` directly and reads
    raw `INPUT_RECORD`s via `ReadConsoleInput`/`PeekConsoleInput`, bypassing whatever
    `System.Console`'s own redirect-detection heuristic was doing (a documented class of
    .NET bug under Windows Terminal/ConPTY).
11. **Device resolution now runs `Set-AudioDevices.ps1` interactively instead of asking
    for a device name as free text** (2026-10-09, also from live feedback), for
    whichever device (`mic` for Dictate, `call` for Live/Call) isn't already in
    `scripts/devices.local.json`. Verified for real: with no devices configured, `dotnet
    run -- --mode dictate` correctly detects that, launches `Set-AudioDevices.ps1`
    inheriting this process's own console, and the script's real device list (11 real
    devices on the machine this was tested on) renders correctly in the same window.
    Not verified: actually typing a selection into that inherited prompt and confirming
    the chosen device flows back into the session that follows -- needs a human to type
    a real number at the real prompt, see above for why this session's tools can't do
    that themselves.
12. **The Settings menu's "Set recordings/transcripts folder"** writes to
    `%LOCALAPPDATA%\voice-local-cli\orchestrator-settings.json` and was verified via
    `dotnet build`/`dotnet test` only (`JsonSettingsStore` is Infrastructure, excluded
    from the coverage gate the same as every other real-OS adapter here, see
    `tests/Cli.Tests/TESTING.md`) -- not run for real. Confirm it actually offers the
    current value, accepts a new path, and that the next Dictate/Live/Call session
    really writes its recordings there instead of the default `<repo>\recordings`.
13. **The main menu now loops instead of exiting after a session, and has an explicit
    "Exit" choice** (2026-10-09, reported live: after pressing Esc, the app returned to
    the shell instead of the menu, with no way back in short of relaunching). Dictate/
    Live/Call now sit under a `SelectionPrompt` choice group labelled "Modes" (Spectre's
    `AddChoiceGroup`), visually separated from "Settings"/"Exit" below it, per the same
    live feedback ("die Alltagsmodi und Exit und Settings... etwas besser visuell
    trennen"). Verified: `dotnet build`/`dotnet test` pass, and `--mode` (what the
    desktop shortcuts use) still runs once and exits -- unchanged, deliberately: a
    shortcut should do the one thing it was made for and close, not open a menu loop
    behind it. NOT verified: the interactive menu itself, since nothing about navigating
    a `SelectionPrompt` can be driven from a non-interactive tool session. In particular,
    confirm pressing Enter on the "Modes" group header itself (if that's even reachable)
    doesn't do anything surprising -- the code defensively falls through to redrawing the
    menu via `Enum.TryParse` rather than crashing, but that fallback path itself has
    never been exercised for real.
14. **Transcript lines now render as a muted meta line (time, and the focused window
    for Dictate) followed by the spoken text on its own line**, instead of one
    run-together line (reported live: "die Trennung zwischen welches Fenster ist gerade
    aktiv und dem eigentlichen Text" wasn't clear enough). The parsing regex was checked
    against both real line shapes this needs to handle (`HH:MM:SS [window] text` from
    dictate.py, `HH:MM:SS text` from transcribe_live.py) using .NET's own regex engine,
    confirming `Match.Groups[2].Success` is `false` (not an empty string) when the
    bracketed-window group doesn't apply, not just that the text visually looked right.
    `MaxVisibleLines` dropped from 12 to 8 entries to account for each one now taking
    2-3 terminal rows instead of 1. Not verified: how this actually looks in a real
    terminal, including whether 8 entries is now too few or still too many given a
    typical window height.
15. **Every launch now prints a startup environment check** (`EnvironmentCheck`,
    2026-10-09, from live feedback: the missing-Hugging-Face-token notice used to only
    surface after a whole Call recording had already finished, with no way to know
    beforehand). Lists Python/ffmpeg (required -- a missing one still exits with code 1,
    now gated by this same check instead of two separate inline failures) and the
    configured mic/call devices and Hugging Face token (optional, shown as "not set"
    rather than blocking). Also added a "Set Hugging Face token" item to the Settings
    menu, running `Save-HfToken.ps1` with inherited console stdio the same way "Change
    audio devices" already runs `Set-AudioDevices.ps1`. Verified for real: run directly
    against this checkout's actual state (a configured `devices.local.json`, no stored
    HF token, and -- in the non-interactive tool session this was run from -- `ffmpeg`
    not resolving on `PATH`), the table correctly showed each row's real status and the
    process exited 1 on the missing-ffmpeg row, matching the existing documented
    before-Setup.ps1 behavior in item 7 above, not a regression introduced here. Not
    verified: the table's column alignment/wrapping in an actual interactive terminal
    window (only confirmed through a piped, non-interactive capture, which does not
    necessarily reflect Spectre's real terminal-width detection -- same caveat as item 5),
    and the "Set Hugging Face token" menu item's `Read-Host -AsSecureString` prompt,
    which needs a human to type a real token at a real, inherited console prompt.
16. **Live and Call now print a consent/data-protection warning panel** before recording
    starts (`PrintCallConsentWarning`, 2026-10-09, requested live: these modes should
    warn explicitly that recording a call -- e.g. a Microsoft Teams meeting -- needs
    everyone's consent, mirroring the README's existing `[!WARNING]` callout but where it
    can't be missed mid-session). Verified for real: `--mode call` with a real `ffmpeg`
    on `PATH` (this tool session's own PATH lacks it; temporarily prepended the real
    winget-installed `ffmpeg.exe` directory to confirm) rendered the bordered red panel
    correctly between the environment-check table and "Recording to: ...", and real
    `ffmpeg`/the recording file it produced were both cleaned up (process killed, WAV
    deleted) right after -- the run itself is otherwise untested beyond that, since
    `AnsiConsole.Live`'s cursor handling still can't run in this piped, non-interactive
    tool session (`System.IO.IOException: The handle is invalid` from
    `LegacyConsoleCursor.Show`, unrelated to this change -- the same class of limitation
    as the `SelectionPrompt` menu itself, see above). Not verified: how the panel reads
    and wraps in a real interactive terminal window, same caveat as items 5 and 15.
17. **The installed app crashed immediately with `Could not find 'VoiceLocalCli.slnx'`**
    (found live installing the real v0.1.0 `Setup.exe`) -- see
    `docs/adr/0005-installed-app-bootstraps-the-engine.md` for the full design. Verified
    for real, twice:
    - A throwaway copy of the built exe + a bundled `engine`/`scripts`/`Assets` folder
      (simulating what `release.yml` now produces, in a plain folder with no `.slnx`
      anywhere above it) correctly resolved the engine to the bundled folder next to the
      exe instead of crashing.
    - The actual v0.1.0 install already on this machine
      (`%LOCALAPPDATA%\VoiceLocalCli\current\`), with its binaries and the same bundled
      folders updated in place: correctly detected `IsInstalled = true`, resolved the
      Python venv to the new stable `%LOCALAPPDATA%\voice-local-cli\engine\.venv\`
      location (not the bundled, update-managed folder), correctly showed "missing" for
      both the venv and ffmpeg, and reached the "run setup now?" bootstrap offer.

    Not verified: actually confirming that prompt and watching the bundled `Setup.ps1`
    run end to end (`AnsiConsole.Confirm` fails with "Failed to read input in
    non-interactive mode" in this piped tool session -- the same class of limitation as
    the `SelectionPrompt` menu, see above, needs a human at a real keyboard); the
    "Repair engine setup" Settings item; "Create desktop shortcuts"' installed-mode
    branch (`VelopackLocator.Current`-based target) actually producing a shortcut that
    launches the installed exe and runs a mode; and the real `release.yml` bundling step
    itself, which was only reproduced locally with the same `Copy-Item` commands, not
    run on a hosted `windows-latest` runner yet -- needs a real `vX.Y.Z` tag push.
18. **The real bootstrap confirmation (item 17's main gap) is now confirmed, twice, by
    Frank at a real keyboard (2026-10-10).** First machine: confirmed, `Setup.ps1` ran
    end to end, installed the venv and all packages for real, the app then showed
    `Python engine OK (bootstrapped for v0.1.0)` and no more bootstrap offer -- also
    directly confirmed the resulting venv's `pip list` has `torch`/`transformers`/
    `faster_whisper`/`pyannote.audio` actually installed, and ffmpeg resolving correctly
    once a fresh shell's PATH picked up the winget install. Second machine: confirmed a
    real, previously-unknown bug instead -- `Setup.ps1`'s "Python" step only checked that
    *some* `py`/`python` command was on PATH, not that Python 3.14 specifically was
    registered with the `py` launcher (step 3 hardcodes `py -3.14`). This second machine
    had Python 3.10 pre-installed for something else, so the check passed, 3.14 was
    never installed, and `py -3.14 -m venv $venvDir` then failed with "Requested Python
    version (3.14) not installed" -- compounded by a second bug: the venv-creation step
    didn't check whether that command actually succeeded before printing `[ok] venv
    created` and moving on, so the real error only surfaced several steps later as a
    confusing `CommandNotFoundException` from `pip` trying to call a `python.exe` that
    was never created. Both fixed: the Python step now checks `py -3.14 --version`
    specifically, and venv creation checks `$LASTEXITCODE`/`Test-Path` before declaring
    success. **Update (same day, v0.1.2):** the second machine was re-run with the fix
    and confirmed it -- ffmpeg already present, Python 3.14 correctly detected as
    missing and installed via winget for real (a genuinely fresh install, not a
    simulated one: real winget download/hash-verify/install output), venv created
    successfully. This also confirms the "install Python 3.14 via winget" path noted as
    unverified above.
19. **The pip-install step looked hung, twice, on two different real machines
    (2026-10-10) -- not a bug, but a real UX problem.** `pip install -e "$engineDir"
    --quiet` pulls in `torch`/`transformers`/`pyannote.audio` with zero visible output
    for several minutes; both times it was reported as "nothing happens, can't go back,
    ESC does nothing" (ESC genuinely does nothing here by design -- the bootstrap runs
    `Process.WaitForExit()` synchronously, before the per-session `ConsoleEscapeWatcher`
    is ever wired up; that's a session concept, not a setup-script one). The second time,
    it was closed mid-install after a few minutes of waiting. Confirmed not harmful:
    `Setup.ps1`'s pip install runs unconditionally after the venv exists/was-just-created
    check, so simply running the bootstrap again resumes/completes it -- pip skips
    already-installed packages. Fixed by dropping `--quiet` from the main pip install (pip's
    own `Collecting`/`Downloading`/`Installing` output is the real fix, not a bespoke
    progress bar) plus an explicit "this can take several minutes" notice before it
    starts. **Follow-up, same day:** reported live that the *next* step, "Optional GPU
    acceleration," now looked hung the same way once the main install stopped being
    silent -- it was still fully suppressed (`--quiet 2>&1 | Out-Null`), its own real
    `pip install` of `optimum-intel[openvino]`/`openvino-genai`. Fixed the same way (no
    `--quiet`, no output redirection, plus a heads-up line that failing here is expected/
    fine), and also dropped `--quiet` from the small `pip install --upgrade pip` call
    just above for the same consistency reason -- every step that can meaningfully take
    more than a couple seconds now shows pip's own real progress rather than nothing.
    Not yet verified: that the real, now-visible pip output actually reads as reassuring
    rather than alarming in a real terminal -- needs a human to watch a real run, same as
    every other visual/UX judgment call in this app.
20. **NVIDIA GPU acceleration via CUDA (`_cuda_backend.py`, 2026-10-10)** -- see
    `docs/adr/0006-nvidia-cuda-as-a-second-gpu-backend.md` for the full design (why CUDA's
    own availability can only be confirmed by actually attempting to load a model, the
    CUDA → OpenVINO → CPU priority, the new `--backend cuda` value). Verified: `pytest
    --cov` (131 tests, 87.99% engine coverage, `_cuda_backend.py` itself 100%),
    `ruff check`/`ruff format --check` clean (both run from `src/engine`, matching CI's
    `working-directory`). **No NVIDIA GPU exists on the machine this was built on** -- the
    detection/fallback logic is confirmed only against mocks (no real GPU present means
    `_cuda_backend.try_load_model()`'s real try/except path was also exercised for real,
    just always hitting the "no GPU" branch, same as `_openvino_backend.is_available()`
    always did on this machine before an Intel GPU was ever involved). Needs a human on
    actual NVIDIA hardware to confirm: the cuBLAS/cuDNN prerequisite actually works once
    installed by hand per the new README section, `--backend auto` actually prefers CUDA
    over CPU and produces correct transcriptions, and whether `compute_type="float16"` is
    the right default across different NVIDIA GPU generations (older cards may need
    `int8_float16` instead -- not yet tested either way).
