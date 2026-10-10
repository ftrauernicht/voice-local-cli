"""Optional, faster transcription path via an NVIDIA GPU (CUDA, through faster-whisper's own
`device="cuda"` support) instead of CPU. Falls back to faster-whisper/CPU automatically and
unobtrusively if no NVIDIA GPU, or no usable CUDA/cuDNN runtime, is available -- on a machine
without one nothing changes.

Needs no extra pip package: faster-whisper (an unconditional dependency already) supports
`device="cuda"` natively via CTranslate2. What it does need is the cuBLAS/cuDNN runtime
libraries on the system -- not installed by Setup.ps1 (no clean, reliable pip/winget path on
Windows for them), so this stays a by-hand prerequisite, same as Voicemeeter. See README.md,
"Supported optional GPU acceleration".

Unlike _openvino_backend's is_available(), hardware presence alone doesn't mean CUDA is
usable here: a GPU and driver can be detected while cuBLAS/cuDNN are still missing or
version-mismatched, and that only surfaces when a model is actually loaded onto the device.
is_gpu_present() is therefore just a hint for log messages -- try_load_model() is the real
gate, since attempting the load *is* the only reliable availability check.
"""

import _console as c


def is_gpu_present() -> bool:
    """Cheap hardware-presence check: an NVIDIA GPU (and driver) exists -- not necessarily
    that CUDA/cuDNN is actually usable. See module docstring."""
    try:
        import ctranslate2

        return ctranslate2.get_cuda_device_count() > 0
    except Exception:
        return False


def try_load_model(model_size: str):
    """Attempts to load a faster-whisper model on the GPU via CUDA. Returns None on any
    failure (no GPU, missing/mismatched cuBLAS or cuDNN, out of VRAM, ...) instead of
    raising -- this can't be predicted cheaply ahead of time, so attempting the load itself
    is the real availability check (see module docstring)."""
    from faster_whisper import WhisperModel

    try:
        return WhisperModel(model_size, device="cuda", compute_type="float16")
    except Exception as exc:
        if is_gpu_present():
            c.warn(f"NVIDIA GPU found but CUDA transcription failed to load, falling back: {exc}")
        return None
