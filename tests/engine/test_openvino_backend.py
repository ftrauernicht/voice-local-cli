"""Tests for _openvino_backend.py's chunking/silence-skip logic and hotwords threading,
using a fake pipeline object -- never loads the real ~800 MB model or touches a GPU."""
from pathlib import Path
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


def _write_complete_model(model_dir):
    """A model folder that passes _model_is_complete: both required weight files, each at
    least _MIN_WEIGHT_BYTES."""
    model_dir.mkdir(parents=True, exist_ok=True)
    for name in ov._REQUIRED_WEIGHT_FILES:
        (model_dir / name).write_bytes(b"\0" * ov._MIN_WEIGHT_BYTES)


class TestModelIsComplete:
    def test_true_when_both_weight_files_present_and_sized(self, tmp_path):
        _write_complete_model(tmp_path / "m")
        assert ov._model_is_complete(tmp_path / "m") is True

    def test_false_when_a_weight_file_is_missing(self, tmp_path):
        model_dir = tmp_path / "m"
        model_dir.mkdir()
        (model_dir / ov._REQUIRED_WEIGHT_FILES[0]).write_bytes(b"\0" * ov._MIN_WEIGHT_BYTES)
        # decoder .bin absent -- the half-download case
        assert ov._model_is_complete(model_dir) is False

    def test_false_when_a_weight_file_is_a_truncated_stub(self, tmp_path):
        model_dir = tmp_path / "m"
        model_dir.mkdir()
        for name in ov._REQUIRED_WEIGHT_FILES:
            (model_dir / name).write_bytes(b"\0" * 10)  # present but far below the floor
        assert ov._model_is_complete(model_dir) is False

    def test_false_when_only_small_tokenizer_bin_is_present(self, tmp_path):
        # The exact failure mode: the small tokenizer/detokenizer .bin arrived, the big
        # encoder/decoder weights did not. The old 'any *.bin' check wrongly passed here.
        model_dir = tmp_path / "m"
        model_dir.mkdir()
        (model_dir / "openvino_tokenizer.bin").write_bytes(b"\0" * ov._MIN_WEIGHT_BYTES)
        assert ov._model_is_complete(model_dir) is False


class TestEnsureModel:
    def test_true_when_model_already_complete(self, tmp_path, monkeypatch):
        model_dir = tmp_path / "whisper-large-v3-turbo"
        _write_complete_model(model_dir)
        monkeypatch.setattr(ov, "MODEL_DIR", model_dir)
        with patch("huggingface_hub.snapshot_download") as fake_download:
            assert ov.ensure_model() is True
        fake_download.assert_not_called()  # already complete, no re-download

    def test_downloads_when_not_present(self, tmp_path, monkeypatch):
        model_dir = tmp_path / "whisper-large-v3-turbo"  # does not exist yet
        monkeypatch.setattr(ov, "MODEL_DIR", model_dir)

        def fake_download(*_a, **kwargs):
            _write_complete_model(Path(kwargs["local_dir"]))

        with patch("huggingface_hub.snapshot_download", side_effect=fake_download) as mock_download:
            assert ov.ensure_model() is True
        mock_download.assert_called_once()
        _, kwargs = mock_download.call_args
        assert kwargs["local_dir"] == str(model_dir)

    def test_redownloads_when_model_is_incomplete(self, tmp_path, monkeypatch):
        # A prior interrupted download left only the small tokenizer .bin behind. ensure_model
        # must NOT treat that as done -- it re-downloads (which fills the missing weights).
        model_dir = tmp_path / "whisper-large-v3-turbo"
        model_dir.mkdir()
        (model_dir / "openvino_tokenizer.bin").write_bytes(b"\0" * ov._MIN_WEIGHT_BYTES)
        monkeypatch.setattr(ov, "MODEL_DIR", model_dir)

        def fake_download(*_a, **kwargs):
            _write_complete_model(Path(kwargs["local_dir"]))

        with patch("huggingface_hub.snapshot_download", side_effect=fake_download) as mock_download:
            assert ov.ensure_model() is True
        mock_download.assert_called_once()

    def test_false_and_warns_when_download_leaves_model_incomplete(self, tmp_path, monkeypatch, capsys):
        # Download "succeeds" (no exception) but the weights still aren't there -- must report
        # False, not lie with True the way the old code did.
        model_dir = tmp_path / "whisper-large-v3-turbo"
        monkeypatch.setattr(ov, "MODEL_DIR", model_dir)
        with patch("huggingface_hub.snapshot_download"):  # does nothing, writes no files
            assert ov.ensure_model() is False
        assert "missing or incomplete" in capsys.readouterr().err

    def test_false_and_warns_when_download_fails(self, tmp_path, monkeypatch, capsys):
        model_dir = tmp_path / "whisper-large-v3-turbo"
        monkeypatch.setattr(ov, "MODEL_DIR", model_dir)
        with patch("huggingface_hub.snapshot_download", side_effect=RuntimeError("network down")):
            assert ov.ensure_model() is False
        assert "falling back to CPU" in capsys.readouterr().err


class TestResolveModelDir:
    def test_uses_checkout_when_slnx_present(self, tmp_path):
        repo = tmp_path / "repo"
        engine = repo / "src" / "engine"
        engine.mkdir(parents=True)
        (repo / "VoiceLocalCli.slnx").write_text("")
        result = ov._resolve_model_dir(engine, "C:/ignored/AppData/Local")
        assert result == engine / "ov-models" / "whisper-large-v3-turbo"

    def test_uses_localappdata_when_no_slnx(self, tmp_path):
        # No VoiceLocalCli.slnx above this folder (tmp_path lives under the system temp dir,
        # not the repo) -- the installed-app case.
        engine = tmp_path / "install" / "current" / "engine"
        engine.mkdir(parents=True)
        appdata = tmp_path / "AppData" / "Local"
        result = ov._resolve_model_dir(engine, str(appdata))
        assert result == appdata / "voice-local-cli" / "engine" / "ov-models" / "whisper-large-v3-turbo"

    def test_falls_back_to_home_appdata_when_localappdata_unset(self, tmp_path):
        engine = tmp_path / "install" / "engine"
        engine.mkdir(parents=True)
        result = ov._resolve_model_dir(engine, None)
        assert result.parts[-4:] == ("voice-local-cli", "engine", "ov-models", "whisper-large-v3-turbo")
