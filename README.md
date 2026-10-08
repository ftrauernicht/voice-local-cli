# voice-local-cli

Local, fully offline speech-to-text for your own machine: dictate hands-free into
whatever window has focus (an editor, a chat, an AI coding assistant's prompt box),
or transcribe a call with speaker diarization. No audio ever leaves the machine
except the one-time download of the speech models themselves.

> **Status: early extraction from an internal prototype.** The Python speech engine
> below is working and has been used daily for voice dictation. The planned .NET +
> Spectre.Console orchestrator that will replace the current PowerShell wrapper
> scripts does not exist yet -- see "Roadmap".

## What this is for

The primary use case is **hands-free local dictation**: speak, and the transcribed
text gets typed directly into whatever window is focused -- most usefully, an AI
coding assistant's chat input. A secondary capability, built on the same engine,
is **full call transcription with speaker diarization** (who said what), for anyone
who wants to see what else local speech models can do.

## How it works today

Three Python entry points under `src/engine/`, each reading audio from a WAV file
that `ffmpeg` is recording:

| Mode | Entry point | What it does |
|---|---|---|
| Dictate | `dictate.py` | Records only your own microphone, transcribes on real speech pauses (not a fixed timer), types the result into the focused window. |
| Live | `transcribe_live.py` | Records a mixed call-audio bus (needs Voicemeeter), shows a continuously updating transcript while the call is in progress. |
| Call | `transcribe.py` | Records the same mixed bus, then runs a full, accurate transcription with speaker diarization after the call ends. |

All three use `faster-whisper` (CPU) or OpenVINO GenAI's `WhisperPipeline` (Intel
GPU, used automatically when available) for transcription, `webrtcvad` for pause
detection, and a locally growing hotwords list (`_hotwords.py`) that improves
recognition of names and jargon you use often -- without you having to maintain it
by hand (it asks, once a word recurs often enough).

## Setup

```powershell
.\scripts\Setup.ps1
```

Installs ffmpeg and the Python environment. **Voicemeeter is only needed for Call/Live
mode** -- if you only want hands-free dictation, skip that part of the checklist the
script prints at the end.

## Usage (current, PowerShell-based)

```powershell
# Pick your microphone once
.\scripts\Set-AudioDevices.ps1

# Dictate: speak, text gets typed into whatever window has focus
.\src\engine\.venv\Scripts\python.exe .\src\engine\dictate.py <path-to-growing-wav-file>
```

A proper `voice-local-cli` entry point that records *and* transcribes in one step,
without needing a separate `ffmpeg` invocation, is the orchestrator described
below -- not built yet.

## Requirements

| Component | Required? | Notes |
|---|---|---|
| Python 3.11+ | Yes | The speech engine. |
| `ffmpeg` | Yes | Audio capture, via Windows' `dshow` input. |
| A microphone | Yes | Any normal input device. |
| Intel GPU + OpenVINO | Optional | Significant speedup; falls back to CPU automatically. |
| Voicemeeter | Optional | Only for Call/Live mode (mixing in the remote party's audio). Not needed for Dictate. |
| Hugging Face account/token | Optional | Only for speaker diarization in Call mode. |

## Roadmap

This is being extracted from an internal prototype into a standalone project. See
`docs/adr/` for the architecture decisions once they're recorded. Planned next:

- A real Python package manifest and test suite (none exists yet -- every test so
  far has been a throwaway script).
- A .NET 10 + Spectre.Console console app that replaces the current two-PowerShell-
  windows-per-mode flow with a single, polished interface and a reliable stop
  command (today, closing a window the wrong way can leave `ffmpeg` recording in
  the background unnoticed).
- CI/CD, dependency updates (Renovate), and a diff-scoped test coverage gate.

## License

MIT, see [LICENSE](LICENSE). Third-party speech models downloaded at runtime
(Whisper, pyannote.audio, the forced-alignment models) are **not** covered by this
license and keep their own separate terms -- see `NOTICE` (added once the model
inventory is documented there).
