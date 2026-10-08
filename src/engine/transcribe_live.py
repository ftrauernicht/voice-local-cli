"""Live transcription: reads a WAV file that's still growing while being recorded, in
chunks, and prints new lines continuously, instead of only at the end like transcribe.py.

Deliberately without speaker diarization: per short chunk (a few seconds), pyannote's
results are too unreliable to be useful -- just timestamps, which is faster and avoids
that extra source of error. Diarization stays reserved for the final transcription
(transcribe.py), which sees the whole recording at once.

Uses an Intel GPU via OpenVINO automatically if available (significantly faster than CPU,
see README.md) -- otherwise faster-whisper/CPU as before, with no change for machines
without a matching GPU.
"""
import argparse
import sys
import time
import warnings
from pathlib import Path

import numpy as np

import _console as c
import _hotwords
import _openvino_backend as ov

warnings.filterwarnings("ignore")  # pyannote/numpy warnings would otherwise spam the live window

SAMPLE_RATE = 16000
BYTES_PER_SAMPLE = 2  # 16-bit mono, as recorded by the capture process
TRAILING_MARGIN_SECONDS = 1.0  # safety margin at the recording edge, in case a word there
                                 # hasn't finished being spoken yet


def find_data_offset(path: Path) -> int:
    """Position where the actual samples start in the WAV file.

    Doesn't just assume a fixed 44 bytes (the usual minimal WAV header) -- more robust to
    actually look for the 'data' chunk marker, in case ffmpeg writes an extra chunk.
    """
    with open(path, "rb") as f:
        header = f.read(256)
    idx = header.find(b"data")
    if idx == -1:
        raise ValueError(f"No 'data' chunk found in the WAV header: {path}")
    return idx + 8  # 4 bytes "data" + 4 bytes chunk size


def available_frames(path: Path, data_offset: int) -> int:
    # The size ffmpeg writes into the header isn't reliable while it's still writing
    # (often only corrected on a clean close) -- so compute from the actual file size
    # instead, not wave.getnframes().
    size = path.stat().st_size
    return max(0, size - data_offset) // BYTES_PER_SAMPLE


def read_frame_range_bytes(path: Path, data_offset: int, start_frame: int, end_frame: int) -> bytes:
    """Raw PCM bytes without normalization -- for webrtcvad (needs raw int16 LE frames,
    not floats)."""
    with open(path, "rb") as f:
        f.seek(data_offset + start_frame * BYTES_PER_SAMPLE)
        return f.read((end_frame - start_frame) * BYTES_PER_SAMPLE)


def read_frame_range(path: Path, data_offset: int, start_frame: int, end_frame: int) -> np.ndarray:
    raw = read_frame_range_bytes(path, data_offset, start_frame, end_frame)
    return np.frombuffer(raw, dtype=np.int16).astype(np.float32) / 32768.0


def load_backend(requested: str, language: str, hotwords: str = ""):
    """Selects and loads the transcription backend. Returns a samples -> text function."""
    use_gpu = requested in ("auto", "gpu") and ov.is_available() and ov.ensure_model()
    if requested == "gpu" and not use_gpu:
        c.warn("GPU backend requested but not available -- falling back to CPU.")

    if use_gpu:
        c.info("Loading GPU pipeline (OpenVINO, large-v3-turbo) ...")
        pipeline = ov.load_pipeline()
        c.ok("GPU pipeline ready.")
        return lambda samples: ov.transcribe_chunk(pipeline, samples, language, hotwords)

    model_name = "small"
    c.info(f"Loading CPU model ({model_name}) ...")
    from faster_whisper import WhisperModel

    model = WhisperModel(model_name, device="cpu", compute_type="int8")
    c.ok("CPU model ready.")

    def transcribe_cpu(samples):
        kwargs = {"hotwords": hotwords} if hotwords else {}
        segments, _info = model.transcribe(samples, language=language, vad_filter=True, **kwargs)
        return " ".join(s.text.strip() for s in segments).strip()

    return transcribe_cpu


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("audio", type=Path, help="WAV file currently being written by ffmpeg")
    parser.add_argument("--interval", type=float, default=8.0, help="Seconds between two chunks")
    parser.add_argument("--backend", choices=["auto", "cpu", "gpu"], default="auto",
                         help="auto = use GPU if available, else CPU (default)")
    parser.add_argument("--language", default="de")
    parser.add_argument("--stale-after", type=float, default=20.0,
                         help="Recording is considered finished after this many seconds without file growth")
    parser.add_argument("--out", type=Path, help="Transcript file (default: WAV path with .live.txt)")
    args = parser.parse_args()

    out_path = args.out or args.audio.with_suffix(".live.txt")
    transcribe = load_backend(args.backend, args.language, _hotwords.load_hotword_string())

    while not args.audio.exists():
        time.sleep(0.5)
    while True:
        try:
            if args.audio.stat().st_size > 256:
                break
        except FileNotFoundError:
            pass
        time.sleep(0.5)
    data_offset = find_data_offset(args.audio)

    processed_frames = 0
    last_size = -1
    last_growth_time = time.time()
    margin_frames = int(TRAILING_MARGIN_SECONDS * SAMPLE_RATE)

    c.info(f"Live transcript running ({args.interval:.0f}s chunks) -> {out_path}\n")

    with out_path.open("w", encoding="utf-8") as out_file:
        while True:
            time.sleep(args.interval)

            try:
                cur_size = args.audio.stat().st_size
            except FileNotFoundError:
                break

            if cur_size != last_size:
                last_size = cur_size
                last_growth_time = time.time()
            elif time.time() - last_growth_time > args.stale_after:
                c.info("No new data anymore -- recording appears to have ended.")
                break

            total_frames = available_frames(args.audio, data_offset)
            safe_end = total_frames - margin_frames
            if safe_end <= processed_frames:
                continue

            samples = read_frame_range(args.audio, data_offset, processed_frames, safe_end)
            processed_frames = safe_end
            if samples.size == 0 or np.abs(samples).max() < 0.01:
                continue  # practically silence, don't bother the model

            text = transcribe(samples)
            if not text:
                continue

            line = f"{time.strftime('%H:%M:%S')} {text}"
            print(line)
            sys.stdout.flush()
            out_file.write(line + "\n")
            out_file.flush()

    c.ok("Live transcript finished.")


if __name__ == "__main__":
    main()
