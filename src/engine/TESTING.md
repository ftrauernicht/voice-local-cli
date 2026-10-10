# Testing the engine

```powershell
cd src/engine
pip install -e ".[dev]"
pytest --cov --cov-report=term-missing
```

Coverage config (branch coverage, source, `fail_under = 85`, exclusions) lives in
`pyproject.toml`'s `[tool.coverage.*]` tables, not duplicated here.

## What's in scope for the 85% gate

Everything that's pure logic or exercisable against a mock: VAD-pause gating and the
segment-rotation/file-watching state machine (`dictate.py`), speaker-assignment math and
the even-split fallback (`transcribe.py`), forced-alignment frame-to-span merging
(`_align_backend.py`), hotword tokenization/candidate bookkeeping (`_hotwords.py`), the
`@@STATE:` event contract (`_console.py`), and backend branching/argument-threading logic
(CUDA → OpenVINO → CPU priority, see `_cuda_backend.py`/`_openvino_backend.py`) across all
three entry points, with `faster_whisper.WhisperModel`, OpenVINO's `WhisperPipeline`,
`ctranslate2.get_cuda_device_count`, and `pyannote.audio.Pipeline` all mocked out.

Several `*MainLoopIntegration` test classes drive the real `main()` functions end to end
against small, real WAV files written on a background timer (simulating ffmpeg), with only
the transcription backend and (for `dictate.py`) `pyautogui.write`/window-focus mocked.

**Every test that touches `dictate.py` autouse-patches `pyautogui.write`** (see
`test_dictate.py`'s `no_real_typing` fixture) -- this suite must never actually type into
whatever window has focus on the machine running it, CI or local.

## What's deliberately out of scope, and why

- **Real `ffmpeg`/dshow audio capture.** No audio hardware (or, on a hosted CI runner, often
  no capture devices at all) to record from. The file-watching/segment-rotation logic that
  *consumes* ffmpeg's output is in scope and covered; the capture itself isn't.
- **Real OpenVINO GPU inference, real CUDA GPU inference, and real wav2vec2
  forced-alignment inference.** All three need either matching GPU hardware (Intel or
  NVIDIA) or downloading real model weights (hundreds of MB to a few GB) over the network --
  none of that is available in CI, and pulling it into a fast unit test suite would defeat
  the point of having one. The branching logic that *decides* whether to use a GPU path, and
  falls back cleanly when it isn't available, is in scope and covered (see
  `_openvino_backend.py`'s/`_cuda_backend.py`'s own coverage, and the `ensure_model()`/
  `is_available()`/`try_load_model()` tests using a fake/patched `huggingface_hub`/
  `openvino`/`ctranslate2`/`faster_whisper` rather than the real packages or hardware).
- **`_align_backend._load()`'s real branch.** Patching `transformers.Wav2Vec2Processor`/
  `Wav2Vec2ForCTC` doesn't reliably intercept the module's own lazy-attribute-loading
  machinery (a known `transformers` behavior) -- only the cache-hit shortcut is tested;
  the real load-and-cache path needs an already-warm Hugging Face cache or network access.
- **`align_words()`'s happy path.** Requires a real, loaded wav2vec2 model for a real
  forward pass -- the early-exit paths (unsupported language, blank text) and the pure
  `_merge_frames_to_spans()` logic downstream of that forward pass are covered instead.
- **Real `pyautogui` typing and real window-focus detection (`pygetwindow`).** Both need a
  real, interactive desktop session; `dictate.py`'s logic around them (what gets typed when,
  what gets logged-only on a focus mismatch) is covered against mocks of both.

If a future change makes any of the above newly mockable at reasonable cost (e.g. a fake
audio device, a tiny local test fixture model), move it into scope and update this file --
don't just let coverage silently include or exclude something without a stated reason here.
