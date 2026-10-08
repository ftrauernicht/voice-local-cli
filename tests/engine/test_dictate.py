"""Tests for dictate.py: the segment-rotation/VAD-pause state machine, per-chunk
processing, and the hotword-candidate review prompt. Uses a mocked transcribe backend and
a mocked typing call throughout -- this suite must never actually type into whatever
window has focus on the machine running it."""
import builtins
import math
import struct
import threading
import time
import wave
from pathlib import Path
from unittest.mock import patch

import numpy as np
import pytest

import dictate as d
import _hotwords as h


@pytest.fixture(autouse=True)
def no_real_typing(monkeypatch):
    """Safety net for the whole module: pyautogui.write must never run for real,
    regardless of which test forgets to patch it explicitly."""
    monkeypatch.setattr(d.pyautogui, "write", lambda *a, **k: None)


@pytest.fixture(autouse=True)
def isolated_hotwords(tmp_path, monkeypatch):
    monkeypatch.setattr(h, "HOTWORDS_PATH", tmp_path / "hotwords.local.json")
    monkeypatch.setattr(h, "CANDIDATES_PATH", tmp_path / "hotwords_candidates.local.json")


def _write_wav(path: Path, samples: list[int], sample_rate: int = 16000) -> None:
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sample_rate)
        w.writeframes(struct.pack(f"<{len(samples)}h", *samples))


def _tone(seconds: float, freq: int = 440, amp: int = 8000) -> list[int]:
    n = int(seconds * d.SAMPLE_RATE)
    return [int(amp * math.sin(2 * math.pi * freq * i / d.SAMPLE_RATE)) for i in range(n)]


def _silence(seconds: float) -> list[int]:
    return [0] * int(seconds * d.SAMPLE_RATE)


class TestSegmentPath:
    def test_formats_three_digit_index(self, tmp_path):
        prefix = tmp_path / "dictate_2026-01-01_00-00-00"
        assert d.segment_path(prefix, 0).name == "dictate_2026-01-01_00-00-00_000.wav"
        assert d.segment_path(prefix, 12).name == "dictate_2026-01-01_00-00-00_012.wav"


class TestWaitForSegment:
    def test_returns_once_file_exists_and_has_a_header(self, tmp_path):
        path = tmp_path / "seg_000.wav"

        def create_after_delay():
            time.sleep(0.1)
            _write_wav(path, _tone(0.1))

        threading.Thread(target=create_after_delay, daemon=True).start()
        start = time.time()
        d.wait_for_segment(path)
        assert time.time() - start >= 0.1
        assert path.exists()


class TestProcessAndType:
    def _setup(self, tmp_path, samples):
        path = tmp_path / "seg_000.wav"
        _write_wav(path, samples)
        offset = d.find_data_offset(path)
        return path, offset

    def test_returns_none_for_empty_range(self, tmp_path):
        path, offset = self._setup(tmp_path, _tone(1.0))
        result = d.process_and_type(path, offset, 5, 5, lambda s: "text", open(tmp_path / "log.txt", "w"), "Win")
        assert result is None

    def test_returns_none_for_silence(self, tmp_path):
        path, offset = self._setup(tmp_path, _silence(1.0))
        with open(tmp_path / "log.txt", "w") as f:
            result = d.process_and_type(path, offset, 0, d.SAMPLE_RATE, lambda s: "text", f, "Win")
        assert result is None

    def test_returns_none_when_transcribe_returns_empty(self, tmp_path):
        path, offset = self._setup(tmp_path, _tone(1.0))
        with open(tmp_path / "log.txt", "w") as f:
            result = d.process_and_type(path, offset, 0, d.SAMPLE_RATE, lambda s: "", f, "Win")
        assert result is None

    def test_types_and_returns_text_when_focus_unchanged(self, tmp_path):
        path, offset = self._setup(tmp_path, _tone(1.0))
        typed = []
        with patch.object(d.pyautogui, "write", lambda text, **k: typed.append(text)), \
             patch.object(d, "active_window_title", lambda: "SameWindow"):
            with open(tmp_path / "log.txt", "w") as f:
                result = d.process_and_type(path, offset, 0, d.SAMPLE_RATE, lambda s: "hello", f, "SameWindow")
        assert result == "hello"
        assert typed == ["hello "]

    def test_logs_but_does_not_type_on_focus_change(self, tmp_path):
        path, offset = self._setup(tmp_path, _tone(1.0))
        typed = []
        with patch.object(d.pyautogui, "write", lambda text, **k: typed.append(text)), \
             patch.object(d, "active_window_title", lambda: "NewWindow"):
            with open(tmp_path / "log.txt", "w") as f:
                result = d.process_and_type(path, offset, 0, d.SAMPLE_RATE, lambda s: "hello", f, "OldWindow")
        assert result == "hello"  # still counted for hotword tracking
        assert typed == []  # but never typed

    def test_writes_log_line_regardless_of_focus_match(self, tmp_path):
        path, offset = self._setup(tmp_path, _tone(1.0))
        log_path = tmp_path / "log.txt"
        with patch.object(d, "active_window_title", lambda: "NewWindow"):
            with log_path.open("w") as f:
                d.process_and_type(path, offset, 0, d.SAMPLE_RATE, lambda s: "hello", f, "OldWindow")
        assert "hello" in log_path.read_text(encoding="utf-8")

    def test_emits_transcribing_state(self, tmp_path, capsys):
        path, offset = self._setup(tmp_path, _tone(1.0))
        with patch.object(d, "active_window_title", lambda: "Win"):
            with open(tmp_path / "log.txt", "w") as f:
                d.process_and_type(path, offset, 0, d.SAMPLE_RATE, lambda s: "hello", f, "Win")
        out = capsys.readouterr().out
        assert '@@STATE:{"name": "transcribing"}' in out

    def test_emits_typing_state_with_window(self, tmp_path, capsys):
        path, offset = self._setup(tmp_path, _tone(1.0))
        with patch.object(d, "active_window_title", lambda: "Win"):
            with open(tmp_path / "log.txt", "w") as f:
                d.process_and_type(path, offset, 0, d.SAMPLE_RATE, lambda s: "hello", f, "Win")
        out = capsys.readouterr().out
        assert '"name": "typing"' in out
        assert '"window": "Win"' in out


class TestReviewHotwordCandidates:
    def test_no_prompt_when_nothing_due(self, monkeypatch, capsys):
        monkeypatch.setattr(builtins, "input", lambda *a: pytest.fail("should not prompt"))
        d.review_hotword_candidates(["just one mention of Moritz"])
        # threshold is 3 by default; a single mention never becomes due
        assert "candidates found" not in capsys.readouterr().out

    def test_yes_answer_adds_the_word(self, monkeypatch):
        monkeypatch.setattr(builtins, "input", lambda *a: "y")
        d.review_hotword_candidates(["Moritz Moritz Moritz"])
        assert h.load_hotword_string() == "Moritz"

    def test_never_answer_dismisses_the_word(self, monkeypatch):
        monkeypatch.setattr(builtins, "input", lambda *a: "never")
        d.review_hotword_candidates(["Moritz Moritz Moritz"])
        assert h.load_hotword_string() == ""
        assert h.due_candidates(threshold=1) == []

    def test_blank_answer_skips_without_dismissing(self, monkeypatch):
        monkeypatch.setattr(builtins, "input", lambda *a: "")
        d.review_hotword_candidates(["Moritz Moritz Moritz"])
        assert h.load_hotword_string() == ""
        # not dismissed -- still due next time
        assert len(h.due_candidates(threshold=3)) == 1


class TestMainLoopIntegration:
    """Drives the real main() state machine (pause detection, segment rotation, stale
    detection) against real small WAV files written on a timer, with the transcription
    backend and window focus mocked out. This is the formalized version of the manual
    scripted runs used to validate the VAD rewrite during development."""

    def test_pause_rotation_and_stale_detection(self, tmp_path, monkeypatch, capsys):
        prefix = tmp_path / "dictate_test"
        segment0 = d.segment_path(prefix, 0)
        segment1 = d.segment_path(prefix, 1)

        calls = []
        monkeypatch.setattr(d, "load_backend", lambda requested, language, hotwords="": (lambda s: calls.append(1) or "testword"))
        monkeypatch.setattr(d, "active_window_title", lambda: "TestWindow")
        monkeypatch.setattr(builtins, "input", lambda *a: "")

        def simulate_ffmpeg():
            part1 = _tone(1.0) + _silence(1.0)
            _write_wav(segment0, part1)
            time.sleep(1.5)
            _write_wav(segment1, _tone(0.3))
            time.sleep(1.2)

        threading.Thread(target=simulate_ffmpeg, daemon=True).start()

        monkeypatch.setattr(
            "sys.argv",
            [
                "dictate.py", str(prefix),
                "--pause-seconds", "0.5", "--stale-after", "2",
                "--out", str(tmp_path / "test.dictate.txt"),
            ],
        )
        d.main()

        assert len(calls) >= 1  # at least the pause-triggered chunk was transcribed
        assert not segment0.exists()  # deleted once finished
        assert not segment1.exists()
        out = capsys.readouterr().out
        assert '"name": "listening"' in out
        assert '"name": "finished"' in out

    def test_max_buffer_overflow_fires_without_a_pause(self, tmp_path, monkeypatch):
        prefix = tmp_path / "dictate_overflow"
        segment0 = d.segment_path(prefix, 0)

        calls = []
        monkeypatch.setattr(d, "load_backend", lambda requested, language, hotwords="": (lambda s: calls.append(1) or "testword"))
        monkeypatch.setattr(d, "active_window_title", lambda: "TestWindow")
        monkeypatch.setattr(builtins, "input", lambda *a: "")

        def simulate_ffmpeg():
            _write_wav(segment0, _tone(2.0))
            time.sleep(0.6)
            _write_wav(segment0, _tone(4.0))
            time.sleep(1.2)

        threading.Thread(target=simulate_ffmpeg, daemon=True).start()

        monkeypatch.setattr(
            "sys.argv",
            [
                "dictate.py", str(prefix),
                "--pause-seconds", "5.0",  # deliberately never reached
                "--max-buffer-seconds", "1.5",
                "--stale-after", "1",
                "--out", str(tmp_path / "test.dictate.txt"),
            ],
        )
        d.main()

        # With a 5s pause threshold that's never hit, only the 1.5s overflow safety net
        # can have triggered a transcription.
        assert len(calls) >= 1

    def test_keep_audio_preserves_segments(self, tmp_path, monkeypatch):
        prefix = tmp_path / "dictate_keep"
        segment0 = d.segment_path(prefix, 0)

        monkeypatch.setattr(d, "load_backend", lambda requested, language, hotwords="": (lambda s: "testword"))
        monkeypatch.setattr(d, "active_window_title", lambda: "TestWindow")
        monkeypatch.setattr(builtins, "input", lambda *a: "")

        def simulate_ffmpeg():
            _write_wav(segment0, _tone(1.0))
            time.sleep(1.5)

        threading.Thread(target=simulate_ffmpeg, daemon=True).start()

        monkeypatch.setattr(
            "sys.argv",
            [
                "dictate.py", str(prefix),
                "--pause-seconds", "0.3", "--stale-after", "1",
                "--keep-audio",
                "--out", str(tmp_path / "test.dictate.txt"),
            ],
        )
        d.main()

        assert segment0.exists()
