"""Tests for _hotwords.py -- fully exercisable without any model, just file I/O and
string logic. See CONTRACT.md for why the module exists."""
import pytest

import _hotwords as h


@pytest.fixture(autouse=True)
def isolated_paths(tmp_path, monkeypatch):
    """Points the module at a throwaway directory for every test, so tests never read or
    write the real hotwords.local.json on the developer's machine."""
    monkeypatch.setattr(h, "HOTWORDS_PATH", tmp_path / "hotwords.local.json")
    monkeypatch.setattr(h, "CANDIDATES_PATH", tmp_path / "hotwords_candidates.local.json")
    return tmp_path


class TestTokenize:
    def test_splits_on_non_letters(self):
        assert h.tokenize("Hello, World! Test-case") == ["Hello", "World", "Test", "case"]

    def test_drops_stopwords(self):
        words = h.tokenize("Das ist ein Test mit Moritz und wmux")
        assert "Moritz" in words
        assert "wmux" in words
        assert "Das" not in words  # stopword
        assert "und" not in words  # stopword

    def test_drops_short_words(self):
        assert h.tokenize("Ich zu da Moritz") == ["Moritz"]

    def test_keeps_umlauts(self):
        assert h.tokenize("Grüße an Müller") == ["Grüße", "Müller"]

    def test_empty_text(self):
        assert h.tokenize("") == []

    def test_case_insensitive_stopword_matching(self):
        # STOPWORDS_DE itself is lowercase; tokenize lowercases for the membership check
        # but returns the original casing.
        assert h.tokenize("UND Moritz") == ["Moritz"]


class TestLoadHotwordString:
    def test_no_file_returns_empty_string(self):
        assert h.load_hotword_string() == ""

    def test_joins_with_spaces(self):
        h.add_hotword("Moritz")
        h.add_hotword("wmux")
        assert h.load_hotword_string() == "Moritz wmux"


class TestAddHotword:
    def test_adds_new_word(self):
        h.add_hotword("Moritz")
        assert h.load_hotword_string() == "Moritz"

    def test_is_idempotent_case_insensitively(self):
        h.add_hotword("Moritz")
        h.add_hotword("moritz")
        h.add_hotword("MORITZ")
        assert h.load_hotword_string() == "Moritz"  # only the first spelling kept

    def test_persists_across_loads(self, isolated_paths):
        h.add_hotword("Moritz")
        data = h._read_json(h.HOTWORDS_PATH, {})
        assert data["hotwords"] == ["Moritz"]


class TestUpdateCountsAndDueCandidates:
    def test_empty_session_is_a_noop(self, isolated_paths):
        h.update_counts([])
        assert not isolated_paths.joinpath("hotwords_candidates.local.json").exists()

    def test_counts_accumulate_across_calls(self):
        h.update_counts(["Moritz war da"])
        h.update_counts(["Moritz kam wieder"])
        h.update_counts(["Moritz nochmal"])
        due = h.due_candidates(threshold=3)
        assert ("moritz", {"count": 3, "display": "Moritz", "first_seen": due[0][1]["first_seen"], "last_seen": due[0][1]["last_seen"]}) == due[0]

    def test_below_threshold_not_due(self):
        h.update_counts(["Moritz war da"])
        h.update_counts(["Moritz kam wieder"])
        assert h.due_candidates(threshold=3) == []

    def test_already_added_word_not_due_again(self):
        h.update_counts(["Moritz " * 5])
        h.add_hotword("Moritz")
        assert h.due_candidates(threshold=3) == []

    def test_dismissed_word_not_due_again(self):
        h.update_counts(["Moritz " * 5])
        h.dismiss_candidate("moritz")
        assert h.due_candidates(threshold=3) == []

    def test_sorted_by_count_descending(self):
        h.update_counts(["Moritz " * 3 + "Zenith " * 5])
        due = h.due_candidates(threshold=3)
        assert [key for key, _ in due] == ["zenith", "moritz"]

    def test_display_keeps_first_seen_casing(self):
        h.update_counts(["moritz Moritz MORITZ"])
        due = h.due_candidates(threshold=1)
        assert due[0][1]["display"] == "moritz"


class TestDismissCandidate:
    def test_is_idempotent(self, isolated_paths):
        h.dismiss_candidate("moritz")
        h.dismiss_candidate("moritz")
        data = h._read_json(h.CANDIDATES_PATH, {})
        assert data["dismissed"] == ["moritz"]


class TestReadJsonRobustness:
    def test_corrupt_file_falls_back_to_default(self, isolated_paths):
        isolated_paths.joinpath("hotwords.local.json").write_text("{not valid json", encoding="utf-8")
        assert h.load_hotword_string() == ""
