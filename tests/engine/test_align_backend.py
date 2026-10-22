"""Tests for _align_backend.py's pure logic (span merging, language support) and the
early-exit paths of align_words() that don't require loading the real wav2vec2 model."""
from unittest.mock import MagicMock, patch

import numpy as np
import pytest

import _align_backend as al


class TestIsSupported:
    def test_known_languages(self):
        assert al.is_supported("de") is True
        assert al.is_supported("en") is True

    def test_unknown_language(self):
        assert al.is_supported("fr") is False
        assert al.is_supported("") is False


class TestAlignWordsEarlyExits:
    def test_unsupported_language_returns_empty_without_loading_anything(self):
        # If this tried to load a model, it would raise (no network/model available in
        # a unit test) -- returning [] before that point is the behavior under test.
        assert al.align_words(np.zeros(1600, dtype=np.float32), "hello", "fr") == []

    def test_blank_text_returns_empty(self):
        assert al.align_words(np.zeros(1600, dtype=np.float32), "   ", "de") == []

    def test_empty_text_returns_empty(self):
        assert al.align_words(np.zeros(1600, dtype=np.float32), "", "de") == []


class TestMergeFramesToSpans:
    def test_single_run(self):
        # blank=0; frames [0,0,5,5,5,0,0]
        spans = al._merge_frames_to_spans([0, 0, 5, 5, 5, 0, 0], blank=0)
        assert spans == [(2, 5)]

    def test_multiple_runs(self):
        spans = al._merge_frames_to_spans([0, 3, 3, 0, 7, 0], blank=0)
        assert spans == [(1, 3), (4, 5)]

    def test_adjacent_different_tokens_are_separate_spans(self):
        # No blank between a run of 3s and a run of 4s -- still two spans, since the
        # merge only joins frames with the SAME token.
        spans = al._merge_frames_to_spans([3, 3, 4, 4], blank=0)
        assert spans == [(0, 2), (2, 4)]

    def test_all_blank_returns_no_spans(self):
        assert al._merge_frames_to_spans([0, 0, 0], blank=0) == []

    def test_empty_input(self):
        assert al._merge_frames_to_spans([], blank=0) == []

    def test_run_starting_at_index_zero(self):
        spans = al._merge_frames_to_spans([5, 5, 0], blank=0)
        assert spans == [(0, 2)]


class TestHasEnoughFrames:
    """CTC forced alignment needs frames >= target tokens + adjacent repeats; align_words()
    checks this up front so a too-short segment degrades to even word distribution quietly,
    instead of letting torchaudio raise "targets length is too long for CTC"."""

    def test_enough_when_frames_equal_tokens_without_repeats(self):
        assert al._has_enough_frames(3, [1, 2, 3]) is True

    def test_too_few_when_one_frame_short(self):
        assert al._has_enough_frames(2, [1, 2, 3]) is False

    def test_adjacent_repeat_needs_one_extra_frame(self):
        # [1, 1, 2] has one adjacent repeat -> needs 3 + 1 = 4 frames.
        assert al._has_enough_frames(3, [1, 1, 2]) is False
        assert al._has_enough_frames(4, [1, 1, 2]) is True

    def test_regression_short_segment_from_the_wild(self):
        # The real failure this guards against: 15 target tokens, 2 adjacent repeats, only
        # 16 emission frames ("log_probs length: 16, targets length: 15, repeats: 2").
        # 15 + 2 = 17 > 16, so alignment cannot run.
        ids = [1, 2, 3, 4, 5, 5, 6, 7, 8, 8, 9, 10, 11, 12, 13]
        assert al._has_enough_frames(16, ids) is False
        assert al._has_enough_frames(17, ids) is True

    def test_empty_targets_always_fit(self):
        assert al._has_enough_frames(0, []) is True


class TestLoad:
    """_load()'s real branch (import transformers, download/load the actual wav2vec2
    weights) needs real network access or an already-populated HF cache -- out of scope
    for a unit test, same reasoning as align_words()'s happy path above. The cache-hit
    shortcut, however, is pure dict logic and doesn't touch transformers at all."""

    def test_cache_hit_returns_the_cached_tuple_without_importing_anything(self, monkeypatch):
        sentinel = (object(), object())
        monkeypatch.setitem(al._cache, "de", sentinel)
        assert al._load("de") is sentinel
