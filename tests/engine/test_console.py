"""Tests for _console.py -- output formatting and the @@STATE: event contract (see
CONTRACT.md). Colored human-readable output goes to stderr, status events to stdout."""
import json

import _console as c


class TestHumanReadableOutput:
    def test_info_goes_to_stderr(self, capsys):
        c.info("hello")
        captured = capsys.readouterr()
        assert "hello" in captured.err
        assert captured.out == ""

    def test_ok_has_prefix(self, capsys):
        c.ok("done")
        captured = capsys.readouterr()
        assert "[ok]" in captured.err
        assert "done" in captured.err

    def test_warn_has_prefix(self, capsys):
        c.warn("careful")
        captured = capsys.readouterr()
        assert "[!]" in captured.err

    def test_err_has_prefix(self, capsys):
        c.err("broken")
        captured = capsys.readouterr()
        assert "[error]" in captured.err


class TestStateEvents:
    def test_goes_to_stdout_not_stderr(self, capsys):
        c.state("listening")
        captured = capsys.readouterr()
        assert captured.out != ""
        assert captured.err == ""

    def test_starts_with_reserved_prefix(self, capsys):
        c.state("listening")
        captured = capsys.readouterr()
        assert captured.out.startswith(c.STATE_PREFIX)

    def test_payload_is_valid_json_with_name(self, capsys):
        c.state("typing", window="Visual Studio Code")
        captured = capsys.readouterr()
        line = captured.out.strip()
        payload = json.loads(line[len(c.STATE_PREFIX):])
        assert payload == {"name": "typing", "window": "Visual Studio Code"}

    def test_no_extra_fields_still_valid(self, capsys):
        c.state("finished")
        captured = capsys.readouterr()
        payload = json.loads(captured.out.strip()[len(c.STATE_PREFIX):])
        assert payload == {"name": "finished"}

    def test_single_line_no_embedded_newline(self, capsys):
        c.state("focus_mismatch", text="some\ntext")
        captured = capsys.readouterr()
        # The JSON payload may contain an escaped \n, but the raw stdout line itself
        # (before the trailing print()-added newline) must stay one line -- a consumer
        # reads line by line.
        lines = captured.out.rstrip("\n").split("\n")
        assert len(lines) == 1

    def test_non_ascii_is_not_escaped(self, capsys):
        c.state("typing", window="Grüße")
        captured = capsys.readouterr()
        assert "Grüße" in captured.out
        assert "\\u" not in captured.out
