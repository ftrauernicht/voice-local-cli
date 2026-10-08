"""Tests for _openvino_backend.py's chunking/silence-skip logic and hotwords threading,
using a fake pipeline object -- never loads the real ~800 MB model or touches a GPU."""
from types import SimpleNamespace
from unittest.mock import MagicMock, patch

import numpy as np
import pytest

import _openvino_backend as ov


def _tone(seconds: float, sample_rate: int = 16000, amp: float = 0.5) -> np.ndarray:
    n = int(seconds * sample_rate)
    t = np.arange(n) / sample_rate
    return (amp * np.sin(2 * np.pi * 440 * t)).astype(np.float32)


def _silence(seconds: float, sample_rate: int = 16000) -> np.ndarray:
    return np.zeros(int(seconds * sample_rate), dtype=np.float32)


class FakeResult:
    def __init__(self, text: str):
        self.texts = [text]


class FakeChunk:
    def __init__(self, start_ts, end_ts, text):
        self.start_ts = start_ts
        self.end_ts = end_ts
        self.text = text


class FakeLongResult:
    def __init__(self, chunks):
        self.chunks = chunks


class TestTranscribeChunk:
    def test_strips_and_returns_text(self):
        pipeline = MagicMock()
        pipeline.generate.return_value = FakeResult("  hello world  ")
        assert ov.transcribe_chunk(pipeline, _tone(1.0), "de") == "hello world"

    def test_passes_language_tag(self):
        pipeline = MagicMock()
        pipeline.generate.return_value = FakeResult("x")
        ov.transcribe_chunk(pipeline, _tone(1.0), "en")
        _, kwargs = pipeline.generate.call_args
        assert kwargs["language"] == "<|en|>"

    def test_hotwords_passed_when_present(self):
        pipeline = MagicMock()
        pipeline.generate.return_value = FakeResult("x")
        ov.transcribe_chunk(pipeline, _tone(1.0), "de", hotwords="Moritz")
        _, kwargs = pipeline.generate.call_args
        assert kwargs["hotwords"] == "Moritz"

    def test_no_hotwords_kwarg_when_empty(self):
        pipeline = MagicMock()
        pipeline.generate.return_value = FakeResult("x")
        ov.transcribe_chunk(pipeline, _tone(1.0), "de", hotwords="")
        _, kwargs = pipeline.generate.call_args
        assert "hotwords" not in kwargs


class TestTranscribeLong:
    def test_skips_near_silent_chunks(self):
        pipeline = MagicMock()
        pipeline.generate.return_value = FakeLongResult([])
        samples = _silence(5.0)  # shorter than CHUNK_SECONDS, entirely silent
        segments = ov.transcribe_long(pipeline, samples, "de")
        pipeline.generate.assert_not_called()
        assert segments == []

    def test_processes_a_single_loud_chunk(self):
        pipeline = MagicMock()
        pipeline.generate.return_value = FakeLongResult(
            [FakeChunk(0.0, 1.0, "hello")]
        )
        samples = _tone(2.0)
        segments = ov.transcribe_long(pipeline, samples, "de")
        assert segments == [{"start": 0.0, "end": 1.0, "text": "hello"}]

    def test_splits_audio_longer_than_chunk_seconds(self):
        pipeline = MagicMock()
        pipeline.generate.return_value = FakeLongResult([FakeChunk(0.0, 1.0, "x")])
        # Longer than CHUNK_SECONDS (28s) -- must be split into at least 2 generate() calls.
        samples = _tone(ov.CHUNK_SECONDS + 5.0)
        ov.transcribe_long(pipeline, samples, "de")
        assert pipeline.generate.call_count == 2

    def test_offsets_timestamps_for_later_chunks(self):
        pipeline = MagicMock()
        # Every chunk reports the same relative (0.0, 1.0) span; the second chunk's
        # absolute offset should still differ.
        pipeline.generate.return_value = FakeLongResult([FakeChunk(0.0, 1.0, "x")])
        samples = _tone(ov.CHUNK_SECONDS + 5.0)
        segments = ov.transcribe_long(pipeline, samples, "de")
        assert segments[0]["start"] == 0.0
        assert segments[1]["start"] == pytest.approx(ov.CHUNK_SECONDS)

    def test_drops_empty_text_chunks(self):
        pipeline = MagicMock()
        pipeline.generate.return_value = FakeLongResult(
            [FakeChunk(0.0, 1.0, "   "), FakeChunk(1.0, 2.0, "real text")]
        )
        segments = ov.transcribe_long(pipeline, _tone(2.0), "de")
        assert len(segments) == 1
        assert segments[0]["text"] == "real text"

    def test_hotwords_passed_through(self):
        pipeline = MagicMock()
        pipeline.generate.return_value = FakeLongResult([])
        ov.transcribe_long(pipeline, _tone(2.0), "de", hotwords="Moritz")
        _, kwargs = pipeline.generate.call_args
        assert kwargs["hotwords"] == "Moritz"


class TestIsAvailable:
    def test_false_when_openvino_not_importable(self, monkeypatch):
        import builtins
        real_import = builtins.__import__

        def fake_import(name, *a, **k):
            if name in ("openvino", "openvino_genai"):
                raise ImportError(f"no {name}")
            return real_import(name, *a, **k)

        monkeypatch.setattr(builtins, "__import__", fake_import)
        assert ov.is_available() is False

    def test_true_when_gpu_is_in_available_devices(self, monkeypatch):
        fake_openvino = MagicMock()
        fake_openvino.Core.return_value.available_devices = ["CPU", "GPU"]
        fake_openvino_genai = MagicMock()
        import sys
        monkeypatch.setitem(sys.modules, "openvino", fake_openvino)
        monkeypatch.setitem(sys.modules, "openvino_genai", fake_openvino_genai)
        assert ov.is_available() is True

    def test_false_when_gpu_not_in_available_devices(self, monkeypatch):
        fake_openvino = MagicMock()
        fake_openvino.Core.return_value.available_devices = ["CPU"]
        import sys
        monkeypatch.setitem(sys.modules, "openvino", fake_openvino)
        monkeypatch.setitem(sys.modules, "openvino_genai", MagicMock())
        assert ov.is_available() is False

    def test_false_when_core_construction_raises(self, monkeypatch):
        fake_openvino = MagicMock()
        fake_openvino.Core.side_effect = RuntimeError("no driver")
        import sys
        monkeypatch.setitem(sys.modules, "openvino", fake_openvino)
        monkeypatch.setitem(sys.modules, "openvino_genai", MagicMock())
        assert ov.is_available() is False


class TestEnsureModel:
    def test_true_when_model_already_present(self, tmp_path, monkeypatch):
        model_dir = tmp_path / "whisper-large-v3-turbo"
        model_dir.mkdir()
        (model_dir / "model.bin").write_bytes(b"fake")
        monkeypatch.setattr(ov, "MODEL_DIR", model_dir)
        assert ov.ensure_model() is True

    def test_downloads_when_not_present(self, tmp_path, monkeypatch):
        model_dir = tmp_path / "whisper-large-v3-turbo"  # does not exist yet
        monkeypatch.setattr(ov, "MODEL_DIR", model_dir)
        fake_download = MagicMock()
        with patch("huggingface_hub.snapshot_download", fake_download):
            assert ov.ensure_model() is True
        fake_download.assert_called_once()
        _, kwargs = fake_download.call_args
        assert kwargs["local_dir"] == str(model_dir)

    def test_false_and_warns_when_download_fails(self, tmp_path, monkeypatch, capsys):
        model_dir = tmp_path / "whisper-large-v3-turbo"
        monkeypatch.setattr(ov, "MODEL_DIR", model_dir)
        with patch("huggingface_hub.snapshot_download", side_effect=RuntimeError("network down")):
            assert ov.ensure_model() is False
        assert "falling back to CPU" in capsys.readouterr().err
