"""Transcribes a recording and assigns speakers (faster-whisper + pyannote).

Two transcription paths for the text itself:
- CPU (faster-whisper, large-v3-turbo): comes with word timestamps directly, as before.
- GPU (OpenVINO, --backend gpu/auto): significantly faster, but without word timestamps --
  those are recovered afterwards per segment via Forced Alignment (see
  _align_backend.py). Only for languages with a matching alignment model (as of
  2026-10-09: German, English); for an unsupported language, --backend auto falls back to
  the CPU path automatically.
"""
import argparse
import os
import wave
from pathlib import Path

import numpy as np

import _align_backend as al
import _console as c
import _hotwords
import _openvino_backend as ov


def load_wav_16k_mono(audio_path: Path) -> np.ndarray:
    """Reads a WAV file recorded at 16 kHz, mono, PCM s16le directly.

    Bypasses faster-whisper's own PyAV decoder (`decode_audio`), which fails with the
    `av` version installed here on an incompatible `metadata_errors` argument (as of
    2026-10-08, Python 3.14). Since the recording format is fixed by the capture process,
    a simple, custom decode is more reliable than chasing PyAV/faster-whisper version
    compatibility.
    """
    with wave.open(str(audio_path), "rb") as wf:
        if wf.getnchannels() != 1 or wf.getsampwidth() != 2 or wf.getframerate() != 16000:
            raise ValueError(
                f"Expected 16 kHz/mono/16-bit PCM, got: "
                f"{wf.getframerate()} Hz, {wf.getnchannels()} channel(s), "
                f"{wf.getsampwidth() * 8} bit -- file not from this project's capture process?"
            )
        raw = wf.readframes(wf.getnframes())
    return np.frombuffer(raw, dtype=np.int16).astype(np.float32) / 32768.0


def transcribe(
    audio_path: Path, model_size: str, language: str | None, hotwords: str = ""
) -> tuple[list[dict], list[dict]]:
    """Returns (segments, words) -- words with their own timestamp for speaker assignment,
    segments as a plain fallback when speaker diarization isn't available."""
    from faster_whisper import WhisperModel

    model = WhisperModel(model_size, device="cpu", compute_type="int8")
    audio = load_wav_16k_mono(audio_path)
    kwargs = {"hotwords": hotwords} if hotwords else {}
    segments, _info = model.transcribe(
        audio, language=language, vad_filter=True, word_timestamps=True, **kwargs
    )
    seg_list: list[dict] = []
    words: list[dict] = []
    for s in segments:
        seg_list.append({"start": s.start, "end": s.end, "text": s.text.strip()})
        for w in s.words or []:
            words.append({"start": w.start, "end": w.end, "text": w.word})
    return seg_list, words


def _split_evenly(segment: dict) -> list[dict]:
    """Distributes a segment's words evenly over its duration -- a fallback option when
    Forced Alignment returns nothing for this segment (unsupported characters, audio/text
    don't match up). Less precise than real alignment, but enough for per-word speaker
    assignment."""
    tokens = segment["text"].split()
    if not tokens:
        return []
    duration = segment["end"] - segment["start"]
    step = duration / len(tokens)
    # Leading space, as with align_words()/faster-whisper -- group_by_speaker() joins
    # words without its own separator.
    return [
        {"start": segment["start"] + i * step, "end": segment["start"] + (i + 1) * step, "text": " " + t}
        for i, t in enumerate(tokens)
    ]


def transcribe_gpu(audio_path: Path, language: str, hotwords: str = "") -> tuple[list[dict], list[dict]]:
    """Like transcribe(), but text via GPU (fast, no word timestamps) and word timestamps
    via Forced Alignment per segment (see module docstring above)."""
    audio = load_wav_16k_mono(audio_path)

    c.info("Loading GPU pipeline (OpenVINO, large-v3-turbo) ...")
    pipeline = ov.load_pipeline()
    c.ok("GPU pipeline ready, transcribing ...")
    segments = ov.transcribe_long(pipeline, audio, language, hotwords=hotwords)

    words: list[dict] = []
    sample_rate = 16000
    for seg in segments:
        start_frame = int(seg["start"] * sample_rate)
        end_frame = int(seg["end"] * sample_rate)
        seg_words = al.align_words(audio[start_frame:end_frame], seg["text"], language, sample_rate)
        if seg_words:
            for w in seg_words:
                words.append({"start": seg["start"] + w["start"], "end": seg["start"] + w["end"], "text": w["text"]})
        else:
            words.extend(_split_evenly(seg))

    return segments, words


def diarize(audio_path: Path, hf_token: str) -> list[dict]:
    import torch
    from pyannote.audio import Pipeline

    pipeline = Pipeline.from_pretrained(
        "pyannote/speaker-diarization-3.1", token=hf_token
    )
    # Passing a path instead of a pre-loaded tensor would use torchcodec internally for
    # decoding -- its native DLLs (libtorchcodec_core4/5.dll) don't match the installed
    # ffmpeg version here (as of 2026-10-08). Reading it ourselves (see
    # load_wav_16k_mono) takes torchcodec out of the picture entirely.
    waveform = torch.from_numpy(load_wav_16k_mono(audio_path)).unsqueeze(0)
    output = pipeline({"waveform": waveform, "sample_rate": 16000})
    # pyannote.audio 4.x returns a DiarizeOutput instead of an Annotation directly like
    # 3.x did. .exclusive_speaker_diarization resolves overlapping speakers into
    # non-overlapping segments -- a good fit, since we assign exactly one speaker per
    # Whisper segment anyway (see assign_speaker).
    diarization = output.exclusive_speaker_diarization
    return [
        {"start": turn.start, "end": turn.end, "speaker": speaker}
        for turn, _, speaker in diarization.itertracks(yield_label=True)
    ]


MAX_GAP_SECONDS = 1.0  # bridge small gaps between speaker changes


def assign_speaker(item: dict, turns: list[dict]) -> str:
    mid = (item["start"] + item["end"]) / 2
    best_speaker, best_dist = None, None
    for turn in turns:
        if turn["start"] <= mid <= turn["end"]:
            return turn["speaker"]
        dist = (turn["start"] - mid) if mid < turn["start"] else (mid - turn["end"])
        if best_dist is None or dist < best_dist:
            best_speaker, best_dist = turn["speaker"], dist
    if best_speaker is not None and best_dist <= MAX_GAP_SECONDS:
        return best_speaker
    return "?"


def group_by_speaker(words: list[dict], turns: list[dict]) -> list[dict]:
    """Merges consecutive words from the same speaker into one line."""
    lines: list[dict] = []
    current_speaker: str | None = None
    current_words: list[str] = []
    current_start = 0.0
    for w in words:
        speaker = assign_speaker(w, turns)
        if current_words and speaker != current_speaker:
            lines.append(
                {"start": current_start, "speaker": current_speaker, "text": "".join(current_words).strip()}
            )
            current_words = []
        if not current_words:
            current_start = w["start"]
        current_speaker = speaker
        current_words.append(w["text"])
    if current_words:
        lines.append(
            {"start": current_start, "speaker": current_speaker, "text": "".join(current_words).strip()}
        )
    return lines


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("audio", type=Path)
    parser.add_argument("--model", default="large-v3-turbo")
    parser.add_argument("--backend", choices=["auto", "cpu", "gpu"], default="auto",
                         help="auto = use GPU+alignment if available and the language is supported, else CPU (default)")
    parser.add_argument("--language", default="de")
    parser.add_argument("--no-diarization", action="store_true")
    args = parser.parse_args()

    if not args.audio.exists():
        c.err(f"File not found: {args.audio}")
        raise SystemExit(1)

    use_gpu = (
        args.backend in ("auto", "gpu")
        and al.is_supported(args.language)
        and ov.is_available()
        and ov.ensure_model()
    )
    if args.backend == "gpu" and not use_gpu:
        c.warn("GPU backend requested, but not available or the language isn't supported -- falling back to CPU.")

    hotwords = _hotwords.load_hotword_string()

    if use_gpu:
        c.info(f"Transcribing {args.audio.name} via GPU (OpenVINO) + Forced Alignment ...")
        segments, words = transcribe_gpu(args.audio, args.language, hotwords)
    else:
        c.info(f"Transcribing {args.audio.name} with model {args.model} (CPU) ...")
        segments, words = transcribe(args.audio, args.model, args.language, hotwords)

    turns: list[dict] = []
    if not args.no_diarization:
        hf_token = os.environ.get("HF_TOKEN")
        if not hf_token:
            c.warn("HF_TOKEN not set -- skipping speaker diarization. See README.md.")
        else:
            c.info("Running speaker diarization ...")
            try:
                turns = diarize(args.audio, hf_token)
            except Exception as exc:
                # Diarization is an addition to the transcription, not part of it -- a
                # failure here (e.g. a not-yet-accepted gated HF repo) must not cause the
                # already-finished transcription to be lost.
                c.warn(f"Speaker diarization failed, writing the transcript anyway, without speaker labels: {exc}")

    out_path = args.audio.with_suffix(".txt")
    with out_path.open("w", encoding="utf-8") as f:
        if turns and words:
            # Word-accurate speaker assignment instead of per (often long) Whisper
            # segment -- a speaker change mid-segment no longer gets swallowed.
            for line in group_by_speaker(words, turns):
                ts = f"{int(line['start'] // 60):02d}:{int(line['start'] % 60):02d}"
                f.write(f"{ts} [{line['speaker']}] {line['text']}\n")
        else:
            for seg in segments:
                ts = f"{int(seg['start'] // 60):02d}:{int(seg['start'] % 60):02d}"
                f.write(f"{ts} {seg['text']}\n")

    c.ok(f"Done: {out_path}")


if __name__ == "__main__":
    main()
