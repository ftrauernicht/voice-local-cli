"""Tests for _cuda_backend.py's detection/fallback logic -- never loads a real model or
touches a real GPU."""
import sys
from unittest.mock import MagicMock, patch

import _cuda_backend as cu


class TestIsGpuPresent:
    def test_true_when_a_cuda_device_is_reported(self, monkeypatch):
        fake_ctranslate2 = MagicMock()
        fake_ctranslate2.get_cuda_device_count.return_value = 1
        monkeypatch.setitem(sys.modules, "ctranslate2", fake_ctranslate2)
        assert cu.is_gpu_present() is True

    def test_false_when_no_cuda_device_is_reported(self, monkeypatch):
        fake_ctranslate2 = MagicMock()
        fake_ctranslate2.get_cuda_device_count.return_value = 0
        monkeypatch.setitem(sys.modules, "ctranslate2", fake_ctranslate2)
        assert cu.is_gpu_present() is False

    def test_false_when_ctranslate2_not_importable(self, monkeypatch):
        import builtins

        real_import = builtins.__import__

        def fake_import(name, *a, **k):
            if name == "ctranslate2":
                raise ImportError("no ctranslate2")
            return real_import(name, *a, **k)

        monkeypatch.setattr(builtins, "__import__", fake_import)
        assert cu.is_gpu_present() is False

    def test_false_when_device_count_raises(self, monkeypatch):
        fake_ctranslate2 = MagicMock()
        fake_ctranslate2.get_cuda_device_count.side_effect = RuntimeError("no driver")
        monkeypatch.setitem(sys.modules, "ctranslate2", fake_ctranslate2)
        assert cu.is_gpu_present() is False


class TestTryLoadModel:
    def test_returns_the_model_on_success(self):
        fake_model = MagicMock()
        with patch("faster_whisper.WhisperModel", return_value=fake_model) as mock_cls:
            result = cu.try_load_model("small")
        assert result is fake_model
        mock_cls.assert_called_once_with("small", device="cuda", compute_type="float16")

    def test_returns_none_without_warning_when_no_gpu_at_all(self, capsys, monkeypatch):
        monkeypatch.setattr(cu, "is_gpu_present", lambda: False)
        with patch("faster_whisper.WhisperModel", side_effect=RuntimeError("no cuda device")):
            result = cu.try_load_model("small")
        assert result is None
        # The common case (no NVIDIA GPU at all) must stay silent -- warning here on every
        # single machine without one would be noise, not a useful signal.
        assert capsys.readouterr().err == ""

    def test_returns_none_and_warns_when_gpu_present_but_load_fails(self, capsys, monkeypatch):
        monkeypatch.setattr(cu, "is_gpu_present", lambda: True)
        with patch("faster_whisper.WhisperModel", side_effect=RuntimeError("cuDNN not found")):
            result = cu.try_load_model("small")
        assert result is None
        assert "falling back" in capsys.readouterr().err
