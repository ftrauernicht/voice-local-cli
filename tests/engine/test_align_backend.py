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


class TestLoad:
    """_load()'s real branch (import transformers, download/load the actual wav2vec2
    weights) needs real network access or an already-populated HF cache -- out of scope
    for a unit test, same reasoning as align_words()'s happy path above. The cache-hit
    shortcut, however, is pure dict logic and doesn't touch transformers at all."""

    def test_cache_hit_returns_the_cached_tuple_without_importing_anything(self, monkeypatch):
        sentinel = (object(), object())
        monkeypatch.setitem(al._cache, "de", sentinel)
        assert al._load("de") is sentinel
