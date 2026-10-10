"""Tests for transcribe_live.py's file-reading helpers (pure, no model needed) and
load_backend's GPU/CPU branching logic (mocked, no real model loaded)."""
import struct
import threading
import time
import wave
from pathlib import Path
from unittest.mock import MagicMock, patch

import numpy as np
import pytest

import transcribe_live as tl


def _write_wav(path: Path, samples: list[int], sample_rate: int = 16000) -> None:
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sample_rate)
        w.writeframes(struct.pack(f"<{len(samples)}h", *samples))


@pytest.fixture
def sample_wav(tmp_path) -> Path:
    path = tmp_path / "sample.wav"
    _write_wav(path, [100, -100, 200, -200, 0, 0])
    return path


class TestFindDataOffset:
    def test_finds_standard_header(self, sample_wav):
        offset = tl.find_data_offset(sample_wav)
        # A minimal PCM WAV header is 44 bytes; the 'data' marker starts at byte 36,
        # the actual samples 8 bytes later.
        assert offset == 44

    def test_raises_on_file_without_data_chunk(self, tmp_path):
        path = tmp_path / "not_a_wav.wav"
        path.write_bytes(b"RIFF" + b"\x00" * 100)
        with pytest.raises(ValueError, match="data"):
            tl.find_data_offset(path)


class TestAvailableFrames:
    def test_matches_written_sample_count(self, sample_wav):
        offset = tl.find_data_offset(sample_wav)
        assert tl.available_frames(sample_wav, offset) == 6

    def test_zero_when_file_shorter_than_offset(self, tmp_path):
        path = tmp_path / "truncated.wav"
        path.write_bytes(b"\x00" * 10)
        assert tl.available_frames(path, 44) == 0


class TestReadFrameRange:
    def test_bytes_variant_returns_raw_int16_le(self, sample_wav):
        offset = tl.find_data_offset(sample_wav)
        raw = tl.read_frame_range_bytes(sample_wav, offset, 0, 2)
        assert raw == struct.pack("<2h", 100, -100)

    def test_float_variant_is_normalized(self, sample_wav):
        offset = tl.find_data_offset(sample_wav)
        samples = tl.read_frame_range(sample_wav, offset, 0, 2)
        assert samples.dtype == np.float32
        assert samples[0] == pytest.approx(100 / 32768.0)
        assert samples[1] == pytest.approx(-100 / 32768.0)

    def test_partial_range(self, sample_wav):
        offset = tl.find_data_offset(sample_wav)
        samples = tl.read_frame_range(sample_wav, offset, 2, 4)
        assert len(samples) == 2
        assert samples[0] == pytest.approx(200 / 32768.0)


class TestLoadBackendBranching:
    """Priority for auto/gpu is CUDA, then OpenVINO, then CPU (see load_backend's own doc
    comment) -- every test here explicitly controls transcribe_live.cu (the _cuda_backend
    module) too, not just .ov, so a test isn't accidentally "passing" only because this
    machine happens to have no real NVIDIA GPU for cu.try_load_model's real fallback to
    hit (see _cuda_backend.py's own tests for that fallback in isolation)."""

    def test_cpu_requested_never_touches_gpu(self):
        with patch("transcribe_live.ov") as mock_ov, \
             patch("transcribe_live.cu") as mock_cu, \
             patch("faster_whisper.WhisperModel") as mock_model_cls:
            mock_model_cls.return_value = MagicMock()
            tl.load_backend("cpu", "de")
            mock_ov.is_available.assert_not_called()
            mock_cu.try_load_model.assert_not_called()

    def test_cuda_requested_but_unavailable_warns_and_falls_back_to_cpu(self, capsys):
        with patch("transcribe_live.ov") as mock_ov, \
             patch("transcribe_live.cu") as mock_cu, \
             patch("faster_whisper.WhisperModel") as mock_model_cls:
            mock_cu.try_load_model.return_value = None
            mock_model_cls.return_value = MagicMock()
            tl.load_backend("cuda", "de")
            captured = capsys.readouterr()
            assert "falling back to CPU" in captured.err
            # An explicit "cuda" request never falls through to OpenVINO -- only "auto"/"gpu" do.
            mock_ov.is_available.assert_not_called()

    def test_gpu_requested_falls_back_to_cpu_when_neither_gpu_works(self, capsys):
        with patch("transcribe_live.ov") as mock_ov, \
             patch("transcribe_live.cu") as mock_cu, \
             patch("faster_whisper.WhisperModel") as mock_model_cls:
            mock_cu.try_load_model.return_value = None
            mock_ov.is_available.return_value = False
            mock_model_cls.return_value = MagicMock()
            tl.load_backend("gpu", "de")
            captured = capsys.readouterr()
            assert "falling back to CPU" in captured.err

    def test_auto_prefers_cuda_when_available(self):
        with patch("transcribe_live.cu") as mock_cu, patch("transcribe_live.ov") as mock_ov:
            fake_model = MagicMock()
            fake_model.transcribe.return_value = ([MagicMock(text=" hi")], None)
            mock_cu.try_load_model.return_value = fake_model

            transcribe = tl.load_backend("auto", "de")
            result = transcribe(np.zeros(16000, dtype=np.float32))

            # CUDA won outright -- OpenVINO's own availability is never even checked.
            mock_ov.is_available.assert_not_called()
            assert result == "hi"

    def test_auto_prefers_openvino_when_cuda_unavailable(self):
        with patch("transcribe_live.cu") as mock_cu, patch("transcribe_live.ov") as mock_ov:
            mock_cu.try_load_model.return_value = None
            mock_ov.is_available.return_value = True
            mock_ov.ensure_model.return_value = True
            mock_pipeline = MagicMock()
            mock_ov.load_pipeline.return_value = mock_pipeline
            mock_ov.transcribe_chunk.return_value = "hello"

            transcribe = tl.load_backend("auto", "de")
            samples = np.zeros(16000, dtype=np.float32)
            result = transcribe(samples)

            mock_ov.transcribe_chunk.assert_called_once()
            assert result == "hello"

    def test_auto_falls_back_to_cpu_when_neither_gpu_works(self):
        with patch("transcribe_live.ov") as mock_ov, \
             patch("transcribe_live.cu") as mock_cu, \
             patch("faster_whisper.WhisperModel") as mock_model_cls:
            mock_cu.try_load_model.return_value = None
            mock_ov.is_available.return_value = True
            mock_ov.ensure_model.return_value = False  # download failed
            mock_model_cls.return_value = MagicMock()

            tl.load_backend("auto", "de")

            mock_ov.load_pipeline.assert_not_called()
            mock_model_cls.assert_called_once()

    def test_hotwords_passed_through_on_cpu_path(self):
        with patch("transcribe_live.ov") as mock_ov, \
             patch("transcribe_live.cu") as mock_cu, \
             patch("faster_whisper.WhisperModel") as mock_model_cls:
            mock_ov.is_available.return_value = False
            mock_cu.try_load_model.return_value = None
            mock_model = MagicMock()
            mock_model.transcribe.return_value = ([], None)
            mock_model_cls.return_value = mock_model

            transcribe = tl.load_backend("cpu", "de", hotwords="Moritz wmux")
            transcribe(np.zeros(1600, dtype=np.float32))

            _, kwargs = mock_model.transcribe.call_args
            assert kwargs["hotwords"] == "Moritz wmux"

    def test_no_hotwords_kwarg_when_empty(self):
        with patch("transcribe_live.ov") as mock_ov, \
             patch("transcribe_live.cu") as mock_cu, \
             patch("faster_whisper.WhisperModel") as mock_model_cls:
            mock_ov.is_available.return_value = False
            mock_cu.try_load_model.return_value = None
            mock_model = MagicMock()
            mock_model.transcribe.return_value = ([], None)
            mock_model_cls.return_value = mock_model

            transcribe = tl.load_backend("cpu", "de", hotwords="")
            transcribe(np.zeros(1600, dtype=np.float32))

            _, kwargs = mock_model.transcribe.call_args
            assert "hotwords" not in kwargs


class TestMainLoopIntegration:
    """Drives the real main() loop (fixed-interval chunking, stale detection) against a
    real small, growing WAV file written on a timer, with the transcription backend
    mocked out."""

    def test_transcribes_chunks_and_stops_on_stale(self, tmp_path, monkeypatch, capsys):
        audio_path = tmp_path / "live.wav"
        calls = []
        monkeypatch.setattr(tl, "load_backend", lambda requested, language, hotwords="": (lambda s: calls.append(1) or "live text"))

        def simulate_ffmpeg():
            samples = [8000] * 16000 + [-8000] * 16000  # 2s of non-silent audio
            _write_wav(audio_path, samples)
            time.sleep(0.8)

        threading.Thread(target=simulate_ffmpeg, daemon=True).start()

        monkeypatch.setattr(
            "sys.argv",
            [
                "transcribe_live.py", str(audio_path),
                "--interval", "0.3", "--stale-after", "1",
                "--out", str(tmp_path / "test.live.txt"),
            ],
        )
        tl.main()

        assert len(calls) >= 1
        out = capsys.readouterr().out
        assert "live text" in out
        log = (tmp_path / "test.live.txt").read_text(encoding="utf-8")
        assert "live text" in log

    def test_skips_silent_chunks(self, tmp_path, monkeypatch):
        audio_path = tmp_path / "live.wav"
        calls = []
        monkeypatch.setattr(tl, "load_backend", lambda requested, language, hotwords="": (lambda s: calls.append(1) or "should not happen"))

        def simulate_ffmpeg():
            _write_wav(audio_path, [0] * 32000)  # pure silence
            time.sleep(0.8)

        threading.Thread(target=simulate_ffmpeg, daemon=True).start()

        monkeypatch.setattr(
            "sys.argv",
            [
                "transcribe_live.py", str(audio_path),
                "--interval", "0.3", "--stale-after", "1",
                "--out", str(tmp_path / "test.live.txt"),
            ],
        )
        tl.main()

        assert calls == []
