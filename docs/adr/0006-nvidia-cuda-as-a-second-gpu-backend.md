# ADR-0006: NVIDIA GPU acceleration via CUDA, alongside Intel/OpenVINO

- **Status**: Accepted
- **Date**: 2026-10-10

## Context

Asked whether an NVIDIA GPU is supported (the engine already has one optional accelerated
path, Intel GPUs via OpenVINO -- `src/engine/_openvino_backend.py`), and for a documented,
extensible list of supported optional GPUs rather than a one-off answer.

Researched directly against the real `faster-whisper`/CTranslate2 project (its live GitHub
README, not assumed from memory): `faster-whisper`'s own `WhisperModel` already supports
`device="cuda"` natively via CTranslate2 -- `faster-whisper` is already an unconditional
dependency (`src/engine/pyproject.toml`), so this needs **no new pip package**. The real
prerequisite is system-level: cuBLAS for CUDA 12 + cuDNN 9 for CUDA 12. On Windows there is
no clean pip-only install path for those -- the official README sends Windows users to a
third-party prebuilt-DLL archive, or the full CUDA Toolkit GUI installer. Unlike OpenVINO,
`scripts/Setup.ps1` can't automate this; it stays a by-hand prerequisite, same category as
Voicemeeter.

Also confirmed directly from `transcribe.py`'s existing code: its CPU path already produces
word-level timestamps natively (`word_timestamps=True`). A CUDA path reusing the same
`WhisperModel.transcribe()` call shape gets them too, for free -- unlike OpenVINO, which
needs the separate Forced Alignment recovery step (`_align_backend.py`) and is restricted to
languages that step supports (German/English today). CUDA has no such restriction.

## Decision

Added NVIDIA as a second optional GPU backend, prioritized ahead of Intel when both are
usable on the same machine (a discrete GPU is typically faster than integrated graphics for
this workload):

- **`src/engine/_cuda_backend.py`** (new, deliberately small -- unlike
  `_openvino_backend.py`, CUDA needs no separate pipeline/chunking logic, just the existing
  `faster_whisper.WhisperModel` call with a different `device`):
  - `is_gpu_present()`: a cheap `ctranslate2.get_cuda_device_count() > 0` hardware-presence
    check, used only for log messages.
  - `try_load_model(model_size)`: attempts `WhisperModel(model_size, device="cuda",
    compute_type="float16")` in a try/except, returning `None` on any failure. **This is the
    real availability gate**, not `is_gpu_present()` -- a GPU and driver can be detected
    while cuBLAS/cuDNN are still missing or version-mismatched, and that failure only
    surfaces when a model is actually loaded. Unlike OpenVINO's `is_available()`, CUDA's
    availability can't be cheaply pre-checked; attempting the load *is* the check.
- **Selection priority** for `--backend auto`/`gpu`, in both `transcribe_live.py`'s
  `load_backend()` (already shared by `dictate.py` via import) and `transcribe.py`'s
  `main()`: NVIDIA (CUDA) first, then Intel (OpenVINO, still gated on Forced Alignment
  language support in `transcribe.py` only -- CUDA has no such gate), then CPU.
- **`--backend` gained an explicit `cuda` value** (alongside existing `auto`/`cpu`/`gpu`) for
  forcing/troubleshooting a specific vendor. An explicit `cuda` request that fails warns and
  falls straight to CPU -- it does not also try OpenVINO, matching how an explicit `gpu`
  request today only warns once if *no* accelerated backend worked.
- **No `pyproject.toml` change** -- confirmed from the official `faster-whisper` README that
  the pip-installable `nvidia-cublas-cu12`/`nvidia-cudnn-cu12` packages are explicitly scoped
  "Linux only"; Windows uses system-level DLLs on `PATH` instead, outside pip's reach.
- **README's "Supported optional GPU acceleration"** table is the extensible list asked
  for: Intel (automatic) and NVIDIA (manual prerequisite) today, an explicitly open row for
  future backends (AMD/ROCm or others) pointing at `_cuda_backend.py`/`_openvino_backend.py`
  as the pattern to follow. A plain table a future PR extends by adding a row plus a small
  matching module -- not a generic plugin registry, unjustified complexity for two real
  backends.
- **No `NOTICE` change** -- CUDA reuses the exact same Whisper `large-v3-turbo` model already
  listed for the CPU path, just a different `device`.

## Consequences

- `transcribe.py`'s CPU-only `transcribe()` function was split into `transcribe_with_model()`
  (takes an already-loaded model, shared by CPU and CUDA) + a thin `transcribe()` wrapper
  that builds the CPU model and delegates -- kept `transcribe()`'s own signature and
  behavior unchanged so existing callers/tests needed no changes beyond the new CUDA
  coverage itself. `transcribe_live.py`'s CPU/CUDA paths were similarly unified behind a new
  `_build_transcribe()` helper.
- Considered fully unifying the two entry points' backend-*selection* orchestration (not
  just the backend modules) into one shared function. Didn't: `transcribe_live.py`/
  `dictate.py` need a reusable per-chunk `samples -> text` callable (streaming), while
  `transcribe.py` needs a one-shot `audio file -> (segments, words)` call with full
  diarization-ready output -- genuinely different shapes that predate this change (even the
  existing CPU/OpenVINO split was never unified across the two). Forcing one shared
  orchestration function would have meant restructuring already-working code beyond what
  this decision needs. The real duplication risk -- loading/detecting each backend -- is
  solved at the module level (`_cuda_backend.py`/`_openvino_backend.py`, used identically by
  both entry points); the remaining ~15-line selection glue stays written twice, same as it
  already was for CPU/OpenVINO before this change.
- Tests mock `_cuda_backend.try_load_model`/`is_gpu_present` the same way existing tests
  already mock `_openvino_backend.is_available` -- real CUDA inference is excluded from the
  85% coverage gate for the same reason real OpenVINO GPU inference already is (no GPU
  hardware on hosted CI runners); the selection/fallback *logic* stays fully covered.
- **No NVIDIA GPU exists on the machine this was built and tested on.** The detection/
  fallback logic was verified against mocks only; the real cuBLAS/cuDNN interaction, and
  whether `compute_type="float16"` is the right default across different NVIDIA GPU
  generations, needs confirmation on real hardware -- see `docs/MANUAL_VERIFICATION.md`.
