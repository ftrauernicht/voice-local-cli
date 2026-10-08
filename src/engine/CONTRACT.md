# Engine CLI contract

This documents the interface between `src/engine/`'s Python scripts and anything that
launches them as a subprocess -- today, manual invocation; planned, the .NET orchestrator
(see the repo README's "Roadmap"). Treat a change here as a breaking change for that
consumer, even though nothing in this repo currently enforces it automatically.

## Entry points

| Script | Positional arg | Purpose |
|---|---|---|
| `dictate.py` | `prefix` -- path prefix of ffmpeg's rotating segment files (`<prefix>_000.wav`, `_001.wav`, ...), without the `_NNN.wav` suffix | Pause-based dictation: transcribes on real speech pauses, types into the focused window. |
| `transcribe_live.py` | `audio` -- path to a single WAV file still being written | Fixed-interval live transcript of a growing recording. |
| `transcribe.py` | `audio` -- path to a finished WAV file | Full, accurate transcription with optional speaker diarization. |

Run `<script>.py --help` for the full flag list (chunking/pause tuning, `--backend`,
`--language`, output paths) -- flags are considered part of this contract too; renaming one
without updating this file and any consumer is the kind of change this document exists to
prevent.

## Output streams

- **stderr**: human-readable, colored status lines (`_console.py`'s `info`/`ok`/`warn`/
  `err`). Free to reword at any time -- never parse these for program logic, they exist for
  a human watching the terminal.
- **stdout**: two kinds of line, both newline-terminated UTF-8:
  - **Transcript lines** -- plain text, the actual recognized speech (dictate.py also
    prefixes a timestamp and the window title, e.g. `14:32:05 [Visual Studio Code] hello
    world`). Format may still evolve; not yet frozen by this contract.
  - **Status-event lines** -- start with the literal prefix `@@STATE:`, followed by one
    JSON object with at least a `"name"` key, no embedded newlines. This prefix is
    reserved and guaranteed stable: a consumer can safely treat `line.startswith("@@STATE:")`
    as "this line is a status event, everything else on stdout is transcript content,"
    without parsing prose. See `_console.state()` for the emitting side.

## Status-event vocabulary (as of this writing)

| `name` | Emitted by | Extra fields | Meaning |
|---|---|---|---|
| `listening` | dictate.py | -- | Waiting for speech; nothing is being transcribed right now. |
| `transcribing` | dictate.py | -- | A speech chunk (ended by a pause or the max-buffer safety net) is being transcribed. |
| `typing` | dictate.py | `window` (str) | Transcribed text is being typed into the named focused window. |
| `focus_mismatch` | dictate.py | `expected`, `actual` (str), `text` (str) | Focus changed since the chunk started; the text was logged but NOT typed. |
| `recording` | transcribe_live.py | -- | Waiting for the next fixed-interval chunk. |
| `transcribing_chunk` | transcribe_live.py | -- | The current interval's audio is being transcribed. |
| `transcribing_full` | transcribe.py | -- | The whole recording is being transcribed (CPU or GPU+alignment path). |
| `diarizing_speakers` | transcribe.py | -- | Speaker diarization is running, after transcription. |
| `finished` | all three | `output` (str, transcribe.py only) | The script is about to exit normally. |

Exit codes: `0` on success; `1` on a handled, reported error (e.g. missing input file --
see each script's `c.err(...)` call sites); an unhandled Python exception exits non-zero
with a traceback on stderr, same as any Python script.

## Adding a new status event

Call `_console.state("your_name", **extra_fields)` from the relevant script, and add a row
to the table above in the same change. Keep field values JSON-serializable primitives
(str/int/float/bool) -- the consumer is a hand-written JSON parser on the other side, not a
schema-validated API.
