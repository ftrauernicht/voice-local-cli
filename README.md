# voice-local-cli

[![CI](https://github.com/ftrauernicht/voice-local-cli/actions/workflows/ci.yml/badge.svg)](https://github.com/ftrauernicht/voice-local-cli/actions/workflows/ci.yml)
[![Format](https://github.com/ftrauernicht/voice-local-cli/actions/workflows/format.yml/badge.svg)](https://github.com/ftrauernicht/voice-local-cli/actions/workflows/format.yml)
[![Security](https://github.com/ftrauernicht/voice-local-cli/actions/workflows/security.yml/badge.svg)](https://github.com/ftrauernicht/voice-local-cli/actions/workflows/security.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Renovate enabled](https://img.shields.io/badge/renovate-enabled-brightgreen.svg)](renovate.json)

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

> [!WARNING]
> Call and Live mode record and transcribe **the full audio of a call**, not just your
> own microphone. In Germany, recording or transcribing another person's spoken words
> without their knowledge is a criminal offense under § 201 StGB regardless of what the
> recording is later used for, and similar consent requirements exist in most other
> jurisdictions. **Only use these modes with the explicit, informed consent of everyone
> on the call.** They are included here because they demonstrate what else a local,
> offline speech pipeline can do, not because this project encourages recording group
> calls. Dictate mode never has this problem: it only ever records your own microphone,
> into your own focused window.

## At a glance

| | |
|---|---|
| **Engine** | Python 3.11+, `faster-whisper` (CPU, or NVIDIA GPU via CUDA) or OpenVINO GenAI (Intel GPU), `webrtcvad` for pause detection |
| **Orchestrator** | .NET 10, Spectre.Console, hexagonal architecture (`Domain`/`Application`/`Infrastructure`/`Ui.Console`) |
| **Tests** | pytest (87%+ line coverage) + xUnit v3 (98%+ line coverage on the hexagonal core) -- both gated at 85% in CI |
| **CI/CD** | GitHub Actions: lint + test + coverage (Python, .NET, PowerShell), format, security scanning, Renovate |
| **License** | MIT for this repo's code; third-party speech models keep their own licenses -- see [NOTICE](NOTICE) |

## How it works

Three Python entry points under `src/engine/`, each reading audio from a WAV file that
`ffmpeg` is recording:

| Mode | Entry point | What it does |
|---|---|---|
| Dictate | `dictate.py` | Records only your own microphone, transcribes on real speech pauses (not a fixed timer), types the result into the focused window. |
| Live | `transcribe_live.py` | Records a mixed call-audio bus (needs Voicemeeter), shows a continuously updating transcript while the call is in progress. |
| Call | `transcribe.py` | Records the same mixed bus, then runs a full, accurate transcription with speaker diarization after the call ends. |

All three use `faster-whisper` (CPU, or NVIDIA GPU via CUDA) or OpenVINO GenAI's
`WhisperPipeline` (Intel GPU) for transcription -- whichever accelerated backend is
available is used automatically, see "Supported optional GPU acceleration" below --
`webrtcvad` for pause detection, and a locally growing hotwords list (`_hotwords.py`)
that improves recognition of names and jargon you use often -- without you having to
maintain it by hand (it asks, once a word recurs often enough).

The .NET orchestrator (`src/Cli/`) drives the Python engine as a child process and parses
its structured `@@STATE:` status events (see `src/engine/CONTRACT.md`) to show a live
Spectre.Console view. Closing the console window, or killing it outright, always stops
`ffmpeg` cleanly too: `ConsoleCtrlHandler` catches a graceful window close, and a Win32 Job
Object is the backstop for a hard kill -- `ffmpeg` never keeps running unnoticed in the
background after the orchestrator itself is gone.

## Setup

```powershell
.\scripts\Setup.ps1
```

Installs ffmpeg and the Python environment. **Voicemeeter is only needed for Call/Live
mode** -- if you only want hands-free dictation, skip that part of the checklist the
script prints at the end.

## Usage

```powershell
# Launches the mode-selection menu (Dictate / Live / Call / Settings) -- the first time
# a mode needs a device that isn't configured yet, it runs Set-AudioDevices.ps1 for you,
# in the same window; no separate step needed, and nothing to run by hand first.
dotnet run --project src\Cli\VoiceLocalCli.Ui.Console

# Or skip the menu and go straight into a mode -- what the desktop shortcuts use
dotnet run --project src\Cli\VoiceLocalCli.Ui.Console -- --mode dictate
```

Every launch starts with a short environment check -- Python, ffmpeg, configured audio
devices, and the Hugging Face token -- so a missing piece shows up before you pick a
mode, not partway through a session.

The Settings menu has a "Create desktop shortcuts" option (one `.lnk` per mode, each
with its own icon, that launches straight into that mode without the menu prompt) and a
"Set Hugging Face token" option that stores it DPAPI-encrypted for Call mode's speaker
diarization.

## Requirements

| Component | Required? | Purpose | CPU/GPU-bound | Notes |
|---|---|---|---|---|
| Python 3.11+ | Yes | Runs the speech engine | CPU | |
| `ffmpeg` | Yes | Audio capture | CPU | Via Windows' `dshow` input |
| A microphone | Yes | Audio input | -- | Any normal input device |
| .NET 10 SDK | Yes, for the orchestrator | Builds/runs `src/Cli/` | CPU | Not needed if driving the Python engine directly |
| GPU acceleration | Optional | Faster transcription | GPU | Falls back to CPU automatically if absent -- see "Supported optional GPU acceleration" below |
| Voicemeeter | Optional | Mixes in the remote party's audio | -- | Only for Call/Live mode, never for Dictate |
| Hugging Face account/token | Optional | Speaker diarization | -- | Only for Call mode's `pyannote.audio` pipeline; set via Settings > "Set Hugging Face token" |

## Supported optional GPU acceleration

Every mode falls back to CPU automatically -- none of this is required. When more than one
accelerated backend is usable on the same machine, NVIDIA is tried first (a discrete GPU is
typically faster than integrated graphics for this workload), then Intel.

| Vendor | Backend | Status | Setup |
|---|---|---|---|
| Intel (integrated or Arc) | OpenVINO | Supported, fully automatic | `scripts/Setup.ps1` installs everything |
| NVIDIA | CUDA (via `faster-whisper`) | Supported, manual prerequisite | Install cuBLAS + cuDNN 9 for CUDA 12 yourself ([NVIDIA cuDNN](https://developer.nvidia.com/cudnn), [NVIDIA cuBLAS](https://developer.nvidia.com/cublas)) -- no extra pip install, picked up automatically once present |
| *(open)* | AMD/ROCm or others | Not implemented | Contributions welcome -- `src/engine/_cuda_backend.py`/`_openvino_backend.py` show the pattern a new backend follows |

NVIDIA's path needs no new Python package (`faster-whisper` already supports
`device="cuda"` natively) but, unlike Intel's, has no clean `pip`/`winget`-only install on
Windows for the CUDA runtime itself -- see
[`docs/adr/0006-nvidia-cuda-as-a-second-gpu-backend.md`](docs/adr/0006-nvidia-cuda-as-a-second-gpu-backend.md)
for why. Once the runtime libraries are on `PATH`, `--backend auto` (the default) picks up
CUDA on its own -- no flag needed; `--backend cuda` forces it for troubleshooting.

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
both of which rely on a Meta MMS checkpoint under CC-BY-NC 4.0, a license too restrictive
for this project's own MIT/commercial-friendly stance.

## Updates

The orchestrator checks GitHub Releases for a newer version on startup (see
`docs/adr/0004-velopack-for-auto-updates.md`) -- never automatically, never mid-session:
finding one only shows a notice, and applying it is a separate action in the Settings
menu. Running from a source checkout (`dotnet run`, the normal case while developing)
skips the check entirely, since there's no installed copy to update. Installer downloads
are on the [Releases page](https://github.com/ftrauernicht/voice-local-cli/releases).

See `docs/adr/` for the architecture decisions behind this project, and
`docs/MANUAL_VERIFICATION.md` for what's confirmed only by hand, not by CI.

## License

MIT, see [LICENSE](LICENSE). Third-party speech models downloaded at runtime are **not**
covered by this license and keep their own separate terms -- see [NOTICE](NOTICE).
