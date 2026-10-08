# voice-local-cli

[![CI](https://github.com/ftrauernicht/voice-local-cli/actions/workflows/ci.yml/badge.svg)](https://github.com/ftrauernicht/voice-local-cli/actions/workflows/ci.yml)
[![Format](https://github.com/ftrauernicht/voice-local-cli/actions/workflows/format.yml/badge.svg)](https://github.com/ftrauernicht/voice-local-cli/actions/workflows/format.yml)
[![Security](https://github.com/ftrauernicht/voice-local-cli/actions/workflows/security.yml/badge.svg)](https://github.com/ftrauernicht/voice-local-cli/actions/workflows/security.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Renovate enabled](https://img.shields.io/badge/renovate-enabled-brightgreen.svg)](renovate.json)

> These badges only render once this repo has a GitHub remote and at least one CI run on
> `main` -- both still pending, see "Project status" below.

Local, fully offline speech-to-text for your own machine: dictate hands-free into
whatever window has focus (an editor, a chat, an AI coding assistant's prompt box). No
audio ever leaves the machine except the one-time download of the speech models
themselves.

## What this is for

The primary use case is **hands-free local dictation to control your own tools** --
speak, and the transcribed text gets typed directly into whatever window is focused,
most usefully an AI coding assistant's chat input or a terminal. Everything else this
repo can do (full call recording, live transcription, speaker diarization) exists as a
secondary, technically-curious exploration of the same local speech models, documented
below, but it is explicitly not the feature this project is built around.

### A note on recording other people (Call/Live mode)

Call and Live mode record and transcribe **the full audio of a call**, not just your own
microphone. In Germany, recording or transcribing another person's spoken words without
their knowledge is a criminal offense under § 201 StGB regardless of what the recording
is later used for, and similar consent requirements exist in most other jurisdictions.
**Only use these modes with the explicit, informed consent of everyone on the call.**
They are included here because they demonstrate what else a local, offline speech
pipeline can do, not because this project encourages recording group calls. Dictate mode
never has this problem: it only ever records your own microphone, into your own focused
window.

## Project status

The Python speech engine (`src/engine/`) is complete and has been used daily for voice
dictation, with a real pytest suite and an 85% coverage gate. The .NET 10 +
Spectre.Console orchestrator (`src/Cli/`) that replaces the previous two-PowerShell-
windows-per-mode flow covers **all three modes** -- Dictate, Live, and Call -- each with
its own session type, confirmed end to end with a real microphone (real ffmpeg capture,
a correctly finalized recording, a clean stop, and for Call mode a real transcription
pass). What's still outstanding is specifically the live view's visual correctness and
real typing into a focused window, which need a human at a real keyboard to judge -- see
`docs/MANUAL_VERIFICATION.md`.

This repo is not yet pushed to GitHub. It is being built up locally first; see
`docs/adr/` once decisions are recorded there.

## At a glance

| | |
|---|---|
| **Engine** | Python 3.11+, `faster-whisper` (CPU) or OpenVINO GenAI (Intel GPU), `webrtcvad` for pause detection |
| **Orchestrator** | .NET 10, Spectre.Console, hexagonal architecture (`Domain`/`Application`/`Infrastructure`/`Ui.Console`) |
| **Tests** | pytest (87%+ line coverage) + xUnit v3 (98%+ line coverage on the hexagonal core) -- both gated at 85% in CI |
| **CI/CD** | GitHub Actions: lint + test + coverage (Python, .NET, PowerShell), format, security scanning, Renovate |
| **License** | MIT for this repo's code; third-party speech models keep their own licenses -- see [NOTICE](NOTICE) |

## How it works today

Three Python entry points under `src/engine/`, each reading audio from a WAV file that
`ffmpeg` is recording:

| Mode | Entry point | What it does |
|---|---|---|
| Dictate | `dictate.py` | Records only your own microphone, transcribes on real speech pauses (not a fixed timer), types the result into the focused window. |
| Live | `transcribe_live.py` | Records a mixed call-audio bus (needs Voicemeeter), shows a continuously updating transcript while the call is in progress. |
| Call | `transcribe.py` | Records the same mixed bus, then runs a full, accurate transcription with speaker diarization after the call ends. |

All three use `faster-whisper` (CPU) or OpenVINO GenAI's `WhisperPipeline` (Intel GPU,
used automatically when available) for transcription, `webrtcvad` for pause detection,
and a locally growing hotwords list (`_hotwords.py`) that improves recognition of names
and jargon you use often -- without you having to maintain it by hand (it asks, once a
word recurs often enough).

The .NET orchestrator (`src/Cli/`) drives the Python engine as a child process and parses
its structured `@@STATE:` status events (see `src/engine/CONTRACT.md`) to show a live
Spectre.Console view instead of raw scrolling PowerShell output. It also fixes a real bug
in the old flow: closing a console window via the X button, or killing it outright, used
to leave `ffmpeg` recording in the background unnoticed (confirmed live, once, for 35+
minutes) -- the orchestrator catches both cases (`ConsoleCtrlHandler` for a graceful
close, a Win32 Job Object as a kernel-level backstop for a hard kill) and stops `ffmpeg`
cleanly either way.

## Setup

```powershell
.\scripts\Setup.ps1
```

Installs ffmpeg and the Python environment. **Voicemeeter is only needed for Call/Live
mode** -- if you only want hands-free dictation, skip that part of the checklist the
script prints at the end.

## Usage

```powershell
# Pick your microphone once
.\scripts\Set-AudioDevices.ps1

# Launches the mode-selection menu (Dictate / Live / Call / Settings)
dotnet run --project src\Cli\VoiceLocalCli.Ui.Console

# Or skip the menu and go straight into a mode -- what the desktop shortcuts use
dotnet run --project src\Cli\VoiceLocalCli.Ui.Console -- --mode dictate
```

The Settings menu has a "Create desktop shortcuts" option: one `.lnk` per mode, each
with its own icon, that launches straight into that mode without the menu prompt.

## Requirements

| Component | Required? | Purpose | CPU/GPU-bound | Notes |
|---|---|---|---|---|
| Python 3.11+ | Yes | Runs the speech engine | CPU | |
| `ffmpeg` | Yes | Audio capture | CPU | Via Windows' `dshow` input |
| A microphone | Yes | Audio input | -- | Any normal input device |
| .NET 10 SDK | Yes, for the orchestrator | Builds/runs `src/Cli/` | CPU | Not needed if driving the Python engine directly |
| Intel GPU + OpenVINO | Optional | Faster transcription | GPU | Falls back to CPU automatically if absent |
| Voicemeeter | Optional | Mixes in the remote party's audio | -- | Only for Call/Live mode, never for Dictate |
| Hugging Face account/token | Optional | Speaker diarization | -- | Only for Call mode's `pyannote.audio` pipeline |

## Models and their licenses

This repo's own code is MIT. The speech models it downloads at runtime are not --
each keeps its upstream license, tracked in [NOTICE](NOTICE). Summary:

| Model | Used by | License |
|---|---|---|
| Whisper `large-v3-turbo` (via `faster-whisper`, CPU) | All three modes | MIT (OpenAI) |
| `OpenVINO/whisper-large-v3-turbo-int8-ov` (Intel GPU) | All three modes, when a GPU is available | MIT (re-verify at publish time, see NOTICE) |
| `jonatasgrosman/wav2vec2-large-xlsr-53-{german,english}` | Call mode's word-level timestamps | Apache-2.0 |
| `pyannote/speaker-diarization-3.1` | Call mode's speaker diarization | Gated on Hugging Face; re-verify at publish time, see NOTICE |

This project deliberately avoids models whose license would restrict non-commercial use
or redistribution: the forced-alignment step specifically uses the models above instead
of torchaudio's built-in `MMS_FA` bundle or the `ctc-forced-aligner` package's default,
both of which rely on a Meta MMS checkpoint under CC-BY-NC 4.0 -- not usable for client
work, which real engagements built on this engine would be.

## Roadmap

- A coverage badge that reflects the live percentage, once this repo has a GitHub remote
  to test the mechanism against.
- An auto-update mechanism for the published orchestrator exe (Velopack is the leading
  candidate -- see `docs/adr/` once that decision is recorded; no release pipeline exists
  yet, so there's nothing to update against today).
- See `docs/adr/` for the architecture decisions recorded so far, and
  `docs/MANUAL_VERIFICATION.md` for what's confirmed only by hand, not by CI.

## License

MIT, see [LICENSE](LICENSE). Third-party speech models downloaded at runtime are **not**
covered by this license and keep their own separate terms -- see [NOTICE](NOTICE).
