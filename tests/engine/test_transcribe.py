"""Tests for transcribe.py: the pure speaker-assignment/grouping logic, the WAV loader,
and the even-split fallback -- all without a real Whisper/pyannote model."""
import struct
import sys
import wave
from pathlib import Path
from unittest.mock import MagicMock, patch

import numpy as np
import pytest

import transcribe as t


def _write_wav(path: Path, samples: list[int], channels: int = 1, width: int = 2, rate: int = 16000) -> None:
    with wave.open(str(path), "wb") as w:
        w.setnchannels(channels)
        w.setsampwidth(width)
        w.setframerate(rate)
        w.writeframes(struct.pack(f"<{len(samples)}h", *samples))


class TestLoadWav16kMono:
    def test_reads_correct_sample_values(self, tmp_path):
        path = tmp_path / "test.wav"
        _write_wav(path, [16384, -16384, 0])
        samples = t.load_wav_16k_mono(path)
        assert samples[0] == pytest.approx(0.5, abs=1e-4)
        assert samples[1] == pytest.approx(-0.5, abs=1e-4)
        assert samples[2] == 0.0

    def test_rejects_wrong_sample_rate(self, tmp_path):
        path = tmp_path / "test.wav"
        _write_wav(path, [0, 0], rate=44100)
        with pytest.raises(ValueError, match="16 kHz"):
            t.load_wav_16k_mono(path)

    def test_rejects_stereo(self, tmp_path):
        path = tmp_path / "test.wav"
        _write_wav(path, [0, 0, 0, 0], channels=2)
        with pytest.raises(ValueError):
            t.load_wav_16k_mono(path)


class TestSplitEvenly:
    def test_distributes_words_over_duration(self):
        segment = {"start": 10.0, "end": 12.0, "text": "a b c d"}
        words = t._split_evenly(segment)
        assert len(words) == 4
        assert words[0]["start"] == 10.0
        assert words[0]["end"] == pytest.approx(10.5)
        assert words[-1]["end"] == pytest.approx(12.0)

    def test_leading_space_matches_faster_whisper_convention(self):
        words = t._split_evenly({"start": 0.0, "end": 1.0, "text": "hello world"})
        assert words[0]["text"] == " hello"
        assert words[1]["text"] == " world"

    def test_empty_text_returns_empty_list(self):
        assert t._split_evenly({"start": 0.0, "end": 1.0, "text": "   "}) == []


class TestAssignSpeaker:
    TURNS = [
        {"start": 0.0, "end": 5.0, "speaker": "SPEAKER_00"},
        {"start": 6.0, "end": 10.0, "speaker": "SPEAKER_01"},
    ]

    def test_word_inside_a_turn(self):
        assert t.assign_speaker({"start": 2.0, "end": 2.5}, self.TURNS) == "SPEAKER_00"

    def test_word_in_small_gap_bridges_to_nearest(self):
        # Gap is 5.0-6.0 (1 second), within MAX_GAP_SECONDS -- word at 5.4 is closer to
        # the end of turn 0.
        assert t.assign_speaker({"start": 5.3, "end": 5.5}, self.TURNS) == "SPEAKER_00"

    def test_word_far_from_any_turn_is_unknown(self):
        assert t.assign_speaker({"start": 100.0, "end": 100.5}, self.TURNS) == "?"

    def test_no_turns_at_all_is_unknown(self):
        assert t.assign_speaker({"start": 1.0, "end": 1.5}, []) == "?"


class TestGroupBySpeaker:
    def test_merges_consecutive_same_speaker_words(self):
        turns = [{"start": 0.0, "end": 10.0, "speaker": "SPEAKER_00"}]
        words = [
            {"start": 0.0, "end": 0.5, "text": " hello"},
            {"start": 0.5, "end": 1.0, "text": " world"},
        ]
        lines = t.group_by_speaker(words, turns)
        assert len(lines) == 1
        assert lines[0]["text"] == "hello world"
        assert lines[0]["speaker"] == "SPEAKER_00"

    def test_splits_on_speaker_change(self):
        turns = [
            {"start": 0.0, "end": 1.0, "speaker": "SPEAKER_00"},
            {"start": 1.0, "end": 2.0, "speaker": "SPEAKER_01"},
        ]
        words = [
            {"start": 0.2, "end": 0.5, "text": " hi"},
            {"start": 1.2, "end": 1.5, "text": " there"},
        ]
        lines = t.group_by_speaker(words, turns)
        assert len(lines) == 2
        assert lines[0]["speaker"] == "SPEAKER_00"
        assert lines[1]["speaker"] == "SPEAKER_01"

    def test_empty_words_returns_empty_lines(self):
        assert t.group_by_speaker([], []) == []

    def test_line_start_is_first_words_start(self):
        turns = [{"start": 0.0, "end": 10.0, "speaker": "SPEAKER_00"}]
        words = [{"start": 3.5, "end": 4.0, "text": " hi"}]
        lines = t.group_by_speaker(words, turns)
        assert lines[0]["start"] == 3.5


class TestTranscribeGpuHotwordsThreading:
    def test_hotwords_passed_to_transcribe_long(self, tmp_path):
        path = tmp_path / "x.wav"
        _write_wav(path, [0] * 16000)

        with patch("transcribe.ov") as mock_ov, patch("transcribe.al") as mock_al:
            mock_ov.load_pipeline.return_value = object()
            mock_ov.transcribe_long.return_value = []
            t.transcribe_gpu(path, "de", hotwords="Moritz")

        _, kwargs = mock_ov.transcribe_long.call_args
        assert kwargs["hotwords"] == "Moritz"

    def test_falls_back_to_split_evenly_when_alignment_fails(self, tmp_path):
        path = tmp_path / "x.wav"
        _write_wav(path, [0] * 16000)

        with patch("transcribe.ov") as mock_ov, patch("transcribe.al") as mock_al:
            mock_ov.load_pipeline.return_value = object()
            mock_ov.transcribe_long.return_value = [
                {"start": 0.0, "end": 1.0, "text": "a b"}
            ]
            mock_al.align_words.return_value = []  # alignment failed

            _, words = t.transcribe_gpu(path, "de")

        assert len(words) == 2  # split_evenly's fallback still produced two words


def _fake_whisper_word(start, end, word):
    w = MagicMock()
    w.start, w.end, w.word = start, end, word
    return w


def _fake_whisper_segment(start, end, text, words=None):
    s = MagicMock()
    s.start, s.end, s.text = start, end, text
    s.words = words or []
    return s


class TestTranscribeCpu:
    def test_returns_segments_and_words(self, tmp_path):
        path = tmp_path / "x.wav"
        _write_wav(path, [0] * 16000)

        fake_model = MagicMock()
        fake_segment = _fake_whisper_segment(
            0.0, 1.0, "  hello world  ",
            words=[_fake_whisper_word(0.0, 0.5, " hello"), _fake_whisper_word(0.5, 1.0, " world")],
        )
        fake_model.transcribe.return_value = ([fake_segment], MagicMock())

        with patch("faster_whisper.WhisperModel", return_value=fake_model):
            segments, words = t.transcribe(path, "large-v3-turbo", "de")

        assert segments == [{"start": 0.0, "end": 1.0, "text": "hello world"}]
        assert words == [
            {"start": 0.0, "end": 0.5, "text": " hello"},
            {"start": 0.5, "end": 1.0, "text": " world"},
        ]

    def test_hotwords_passed_through(self, tmp_path):
        path = tmp_path / "x.wav"
        _write_wav(path, [0] * 16000)
        fake_model = MagicMock()
        fake_model.transcribe.return_value = ([], MagicMock())

        with patch("faster_whisper.WhisperModel", return_value=fake_model):
            t.transcribe(path, "large-v3-turbo", "de", hotwords="Moritz")

        _, kwargs = fake_model.transcribe.call_args
        assert kwargs["hotwords"] == "Moritz"

    def test_no_hotwords_kwarg_when_empty(self, tmp_path):
        path = tmp_path / "x.wav"
        _write_wav(path, [0] * 16000)
        fake_model = MagicMock()
        fake_model.transcribe.return_value = ([], MagicMock())

        with patch("faster_whisper.WhisperModel", return_value=fake_model):
            t.transcribe(path, "large-v3-turbo", "de", hotwords="")

        _, kwargs = fake_model.transcribe.call_args
        assert "hotwords" not in kwargs

    def test_segment_without_words_attribute_is_handled(self, tmp_path):
        # s.words can be None (not just an empty list) from faster-whisper's own API.
        path = tmp_path / "x.wav"
        _write_wav(path, [0] * 16000)
        fake_model = MagicMock()
        fake_segment = _fake_whisper_segment(0.0, 1.0, "hi")
        fake_segment.words = None
        fake_model.transcribe.return_value = ([fake_segment], MagicMock())

        with patch("faster_whisper.WhisperModel", return_value=fake_model):
            segments, words = t.transcribe(path, "large-v3-turbo", "de")

        assert segments == [{"start": 0.0, "end": 1.0, "text": "hi"}]
        assert words == []


class TestDiarize:
    def test_converts_pyannote_output_to_turns(self, tmp_path):
        path = tmp_path / "x.wav"
        _write_wav(path, [0] * 16000)

        fake_pipeline_instance = MagicMock()
        fake_output = MagicMock()
        fake_diarization = MagicMock()
        fake_diarization.itertracks.return_value = [
            (MagicMock(start=0.0, end=2.0), None, "SPEAKER_00"),
            (MagicMock(start=2.0, end=4.0), None, "SPEAKER_01"),
        ]
        fake_output.exclusive_speaker_diarization = fake_diarization
        fake_pipeline_instance.return_value = fake_output

        with patch("pyannote.audio.Pipeline.from_pretrained", return_value=fake_pipeline_instance):
            turns = t.diarize(path, "hf_fake_token")

        assert turns == [
            {"start": 0.0, "end": 2.0, "speaker": "SPEAKER_00"},
            {"start": 2.0, "end": 4.0, "speaker": "SPEAKER_01"},
        ]
        # Called with a pre-loaded waveform dict, not a bare path -- this is what keeps
        # torchcodec out of the loading path (see the module docstring).
        call_kwargs = fake_pipeline_instance.call_args[0][0]
        assert call_kwargs["sample_rate"] == 16000
        assert "waveform" in call_kwargs

    def test_passes_the_token_through(self, tmp_path):
        path = tmp_path / "x.wav"
        _write_wav(path, [0] * 16000)
        fake_pipeline_instance = MagicMock()
        fake_output = MagicMock()
        fake_output.exclusive_speaker_diarization.itertracks.return_value = []
        fake_pipeline_instance.return_value = fake_output

        with patch("pyannote.audio.Pipeline.from_pretrained", return_value=fake_pipeline_instance) as mock_from_pretrained:
            t.diarize(path, "hf_specific_token")

        _, kwargs = mock_from_pretrained.call_args
        assert kwargs["token"] == "hf_specific_token"


class TestMain:
    def _run_main(self, argv):
        with patch.object(sys, "argv", argv):
            t.main()

    def test_exits_with_error_when_file_missing(self, tmp_path, capsys, monkeypatch):
        missing = tmp_path / "missing.wav"
        monkeypatch.setattr(sys, "argv", ["transcribe.py", str(missing)])
        with pytest.raises(SystemExit) as exc_info:
            t.main()
        assert exc_info.value.code == 1
        assert "not found" in capsys.readouterr().err.lower()

    def test_cpu_path_without_diarization_writes_segments(self, tmp_path, monkeypatch):
        audio = tmp_path / "call.wav"
        _write_wav(audio, [0] * 16000)
        monkeypatch.setattr(
            sys, "argv",
            ["transcribe.py", str(audio), "--backend", "cpu", "--no-diarization"],
        )
        monkeypatch.setattr(
            t, "transcribe",
            lambda path, model, language, hotwords="": (
                [{"start": 0.0, "end": 1.0, "text": "hello"}], [],
            ),
        )
        monkeypatch.setattr(t._hotwords, "load_hotword_string", lambda: "")

        t.main()

        out_path = audio.with_suffix(".txt")
        content = out_path.read_text(encoding="utf-8")
        assert "hello" in content
        assert content.startswith("00:00")

    def test_gpu_path_selected_when_available_and_supported(self, tmp_path, monkeypatch):
        audio = tmp_path / "call.wav"
        _write_wav(audio, [0] * 16000)
        monkeypatch.setattr(sys, "argv", ["transcribe.py", str(audio), "--no-diarization"])
        monkeypatch.setattr(t.al, "is_supported", lambda lang: True)
        monkeypatch.setattr(t.ov, "is_available", lambda: True)
        monkeypatch.setattr(t.ov, "ensure_model", lambda: True)
        monkeypatch.setattr(t._hotwords, "load_hotword_string", lambda: "")

        gpu_called = []
        monkeypatch.setattr(
            t, "transcribe_gpu",
            lambda path, language, hotwords="": (gpu_called.append(1) or ([{"start": 0.0, "end": 1.0, "text": "x"}], [])),
        )
        cpu_called = []
        monkeypatch.setattr(t, "transcribe", lambda *a, **k: (cpu_called.append(1) or ([], [])))

        t.main()

        assert gpu_called == [1]
        assert cpu_called == []

    def test_diarization_groups_words_by_speaker(self, tmp_path, monkeypatch):
        audio = tmp_path / "call.wav"
        _write_wav(audio, [0] * 16000)
        monkeypatch.setattr(sys, "argv", ["transcribe.py", str(audio), "--backend", "cpu"])
        monkeypatch.setenv("HF_TOKEN", "hf_fake")
        monkeypatch.setattr(t._hotwords, "load_hotword_string", lambda: "")
        monkeypatch.setattr(
            t, "transcribe",
            lambda *a, **k: (
                [],
                [{"start": 0.0, "end": 0.5, "text": " hi"}, {"start": 0.5, "end": 1.0, "text": " there"}],
            ),
        )
        monkeypatch.setattr(
            t, "diarize",
            lambda path, token: [{"start": 0.0, "end": 2.0, "speaker": "SPEAKER_00"}],
        )

        t.main()

        content = audio.with_suffix(".txt").read_text(encoding="utf-8")
        assert "[SPEAKER_00]" in content
        assert "hi there" in content

    def test_diarization_failure_does_not_lose_the_transcript(self, tmp_path, monkeypatch, capsys):
        audio = tmp_path / "call.wav"
        _write_wav(audio, [0] * 16000)
        monkeypatch.setattr(sys, "argv", ["transcribe.py", str(audio), "--backend", "cpu"])
        monkeypatch.setenv("HF_TOKEN", "hf_fake")
        monkeypatch.setattr(t._hotwords, "load_hotword_string", lambda: "")
        monkeypatch.setattr(
            t, "transcribe",
            lambda *a, **k: ([{"start": 0.0, "end": 1.0, "text": "still here"}], []),
        )

        def boom(path, token):
            raise RuntimeError("gated repo not accepted yet")

        monkeypatch.setattr(t, "diarize", boom)

        t.main()

        content = audio.with_suffix(".txt").read_text(encoding="utf-8")
        assert "still here" in content
        assert "Speaker diarization failed" in capsys.readouterr().err

    def test_missing_hf_token_skips_diarization_with_a_warning(self, tmp_path, monkeypatch, capsys):
        audio = tmp_path / "call.wav"
        _write_wav(audio, [0] * 16000)
        monkeypatch.setattr(sys, "argv", ["transcribe.py", str(audio), "--backend", "cpu"])
        monkeypatch.delenv("HF_TOKEN", raising=False)
        monkeypatch.setattr(t._hotwords, "load_hotword_string", lambda: "")
        monkeypatch.setattr(
            t, "transcribe",
            lambda *a, **k: ([{"start": 0.0, "end": 1.0, "text": "hello"}], []),
        )

        t.main()

        assert "HF_TOKEN not set" in capsys.readouterr().err
        assert "hello" in audio.with_suffix(".txt").read_text(encoding="utf-8")

    def test_emits_state_events(self, tmp_path, monkeypatch, capsys):
        audio = tmp_path / "call.wav"
        _write_wav(audio, [0] * 16000)
        monkeypatch.setattr(sys, "argv", ["transcribe.py", str(audio), "--backend", "cpu", "--no-diarization"])
        monkeypatch.setattr(t._hotwords, "load_hotword_string", lambda: "")
        monkeypatch.setattr(t, "transcribe", lambda *a, **k: ([], []))

        t.main()

        out = capsys.readouterr().out
        assert '"name": "transcribing_full"' in out
        assert '"name": "finished"' in out
