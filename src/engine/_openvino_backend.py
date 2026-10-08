"""Optional, faster transcription path via an Intel GPU (OpenVINO GenAI) instead of CPU
(faster-whisper). Falls back to faster-whisper/CPU automatically and unobtrusively if no
matching Intel GPU, no OpenVINO, or no model is available -- on a machine without an Intel
GPU (AMD, Nvidia, no dedicated graphics silicon) nothing changes.

Does not produce word-level timestamps itself (the pre-converted model fails on that with
"Encoder attention heads are not decomposed"). Irrelevant for Live/Dictate (see
transcribe_chunk). For Call mode, transcribe.py recovers word timestamps afterwards via
Forced Alignment per segment -- see _align_backend.py and README.md, "Model choice".
"""
from pathlib import Path

import numpy as np

import _console as c

MODEL_ID = "OpenVINO/whisper-large-v3-turbo-int8-ov"
MODEL_DIR = Path(__file__).parent / "ov-models" / "whisper-large-v3-turbo"


def is_available() -> bool:
    """Checks whether an Intel GPU is reachable via OpenVINO -- without loading or
    downloading anything."""
    try:
        import openvino
        import openvino_genai  # noqa: F401
    except ImportError:
        return False
    try:
        return "GPU" in openvino.Core().available_devices
    except Exception:
        return False


def ensure_model() -> bool:
    """Downloads the pre-converted model once (~800 MB) if it isn't present locally yet.
    Returns False if the download fails -- the CPU fallback then takes over."""
    if MODEL_DIR.exists() and any(MODEL_DIR.glob("*.bin")):
        return True
    try:
        from huggingface_hub import snapshot_download

        c.info(f"Downloading the GPU model once (~800 MB): {MODEL_ID} ...")
        snapshot_download(MODEL_ID, local_dir=str(MODEL_DIR))
        return True
    except Exception as exc:
        c.warn(f"GPU model download failed, falling back to CPU: {exc}")
        return False


def load_pipeline():
    """Loads the OpenVINO GenAI pipeline. Only call once both is_available() and
    ensure_model() have returned True."""
    import openvino_genai

    return openvino_genai.WhisperPipeline(str(MODEL_DIR), device="GPU")


def transcribe_chunk(pipeline, samples, language: str = "de", hotwords: str = "") -> str:
    """Transcribes one audio chunk, without word timestamps (see module docstring).

    `hotwords` works the same way as with faster-whisper (see transcribe_live.py) --
    OpenVINO GenAI's WhisperPipeline accepts the same parameter and applies it to every
    processing window, not just the first the way `initial_prompt` does."""
    lang_tag = f"<|{language}|>"
    kwargs = {"hotwords": hotwords} if hotwords else {}
    result = pipeline.generate(samples.tolist(), language=lang_tag, **kwargs)
    return result.texts[0].strip()


CHUNK_SECONDS = 28.0  # under the model's ~30s context window


def transcribe_long(
    pipeline, samples, language: str = "de", sample_rate: int = 16000, hotwords: str = ""
) -> list[dict]:
    """Transcribes a recording of arbitrary length with segment-level timestamps
    (start/end/text, not word-level -- see _align_backend.align_words for that).

    Splits the recording into CHUNK_SECONDS pieces itself, instead of trusting the
    pipeline's built-in long-form handling: a direct generate() call on a whole,
    multi-minute recording repeated the same text for several consecutive segments
    (2026-10-09, a known behavior of Whisper models beyond the context window, not a bug
    in this code). Splitting it ourselves means the pipeline never sees more than
    CHUNK_SECONDS at a time.

    Skips near-silent chunks (like transcribe_live.py/dictate.py) -- without that, a call
    with a lot of dead air would be slower than the CPU path's VAD filter, which already
    does this automatically (measured on a test call with 91% silence, 2026-10-09).
    """
    chunk_frames = int(CHUNK_SECONDS * sample_rate)
    lang_tag = f"<|{language}|>"
    kwargs = {"hotwords": hotwords} if hotwords else {}
    segments: list[dict] = []
    for chunk_start in range(0, len(samples), chunk_frames):
        chunk = samples[chunk_start: chunk_start + chunk_frames]
        if chunk.size == 0 or np.abs(chunk).max() < 0.01:
            continue
        offset = chunk_start / sample_rate
        result = pipeline.generate(chunk.tolist(), language=lang_tag, return_timestamps=True, **kwargs)
        for seg in result.chunks:
            text = seg.text.strip()
            if text:
                segments.append({"start": offset + seg.start_ts, "end": offset + seg.end_ts, "text": text})
    return segments
