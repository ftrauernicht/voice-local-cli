"""Dictate mode: types continuously transcribed text into whatever window currently has
focus, instead of only writing it to a file (the file is still kept as a log, under
`*.dictate.txt`). Deliberately WITHOUT an automatic Enter afterward -- a mishearing should
at worst leave the wrong text behind, never trigger a command.

Safeguard against a focus change: if the focused window changes between recording and
finishing a chunk (happened live on 2026-10-08 -- a typing attempt landed in the browser
instead of the target window because SetForegroundWindow failed silently), nothing gets
typed, only logged. Focus is never forced programmatically -- that was exactly the root
cause of that bug.

Uses an Intel GPU via OpenVINO automatically if available (significantly faster than CPU,
see README.md) -- otherwise faster-whisper/CPU as before, with no change for machines
without a matching GPU.

Types via pyautogui.write(). German umlauts (ä/ö/ü/ß) get silently skipped, not typed --
pyautogui maps characters through its own table that's fixed to a US keyboard. A
replacement via Windows SendInput (both in Unicode mode and with real, layout-dependent
key codes) was tested on 2026-10-09, but let no text through at all in wmux -- worse than
losing umlauts. See README.md, "Known quirks".

No longer cuts chunks after a fixed time, but at real speech pauses (webrtcvad) -- a
shorter or longer stretch of speech gets cut appropriately, instead of bluntly every few
seconds, mid-word. A safety net (--max-buffer-seconds) still cuts if no pause ever comes
(a long monologue). webrtcvad itself is cheap enough (<1ms per 30ms frame) that CPU/GPU
doesn't matter for it -- the actual transcription still runs on GPU via --backend when
available.

Uses a local hotwords list (_hotwords.py) to improve recognition of frequent names/jargon
(e.g. colleague names, tool names like "wmux"). The list isn't maintained by hand -- it
grows through a prompt at the end of each session, once a word has come up often enough.
"""
import argparse
import sys
import time
import warnings
from pathlib import Path

import numpy as np
import pyautogui
import webrtcvad

import _console as c
import _hotwords
from transcribe_live import (
    find_data_offset, available_frames, read_frame_range, read_frame_range_bytes,
    load_backend, SAMPLE_RATE,
)

pyautogui.FAILSAFE = False
warnings.filterwarnings("ignore")

VAD_FRAME_MS = 30  # webrtcvad only allows 10/20/30ms frames
VAD_FRAME_SAMPLES = SAMPLE_RATE * VAD_FRAME_MS // 1000  # 480
VAD_FRAME_BYTES = VAD_FRAME_SAMPLES * 2  # 16-bit mono
POLL_SECONDS = 0.2  # how often newly available frames are checked


def active_window_title() -> str:
    try:
        import pygetwindow as gw
        win = gw.getActiveWindow()
        return win.title if win else "(unknown)"
    except Exception:
        return "(unknown)"


def segment_path(prefix: Path, index: int) -> Path:
    return prefix.with_name(f"{prefix.name}_{index:03d}.wav")


def wait_for_segment(path: Path) -> None:
    """Waits until ffmpeg has created a new segment and written the WAV header."""
    while not path.exists():
        time.sleep(0.5)
    while True:
        try:
            if path.stat().st_size > 256:
                return
        except FileNotFoundError:
            pass
        time.sleep(0.5)


def process_and_type(
    path: Path, data_offset: int, start_frame: int, end_frame: int,
    transcribe, out_file, window_before: str,
) -> str | None:
    """Transcribes an audio chunk and types it, provided focus hasn't changed since
    `window_before`. Returns the transcribed text (even if it was only logged instead of
    typed), or None if there was nothing usable -- feeds the caller's hotword candidate
    count."""
    if end_frame <= start_frame:
        return None
    samples = read_frame_range(path, data_offset, start_frame, end_frame)
    if samples.size == 0 or np.abs(samples).max() < 0.01:
        return None  # practically silence, don't bother the model

    c.state("transcribing")
    text = transcribe(samples)
    if not text:
        return None

    ts = time.strftime("%H:%M:%S")
    window_now = active_window_title()
    out_file.write(f"{ts} [{window_now}] {text}\n")
    out_file.flush()

    if window_now != window_before:
        c.state("focus_mismatch", expected=window_before, actual=window_now, text=text)
        c.warn(f"{ts} Focus changed ('{window_before}' -> '{window_now}') -- NOT typed, only logged: {text}")
    else:
        print(f"{ts} [{window_now}] {text}")
        c.state("typing", window=window_now)
        pyautogui.write(text + " ", interval=0.01)
    sys.stdout.flush()
    return text


def review_hotword_candidates(session_texts: list[str]) -> None:
    """Counts the session into the candidate bookkeeping and, at the end, asks for every
    word that came up often enough and isn't listed yet whether it should be added to the
    hotwords list. No automatic change without confirmation -- see _hotwords.py."""
    _hotwords.update_counts(session_texts)
    due = _hotwords.due_candidates()
    if not due:
        return

    c.info("\nNew hotword candidates found (otherwise often misrecognized):")
    for key, entry in due:
        answer = input(
            f"  '{entry['display']}' ({entry['count']}x total) -- add to the hotwords list? [y/N/never] "
        ).strip().lower()
        if answer == "y":
            _hotwords.add_hotword(entry["display"])
            c.ok(f"'{entry['display']}' added.")
        elif answer == "never":
            _hotwords.dismiss_candidate(key)
            c.info(f"'{entry['display']}' will no longer be suggested.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("prefix", type=Path,
                         help="Path prefix of the segment files (without '_NNN.wav'), as ffmpeg's -f segment creates them")
    parser.add_argument("--pause-seconds", type=float, default=0.6,
                         help="Silence duration that closes off a chunk")
    parser.add_argument("--vad-aggressiveness", type=int, default=2, choices=[0, 1, 2, 3],
                         help="webrtcvad sensitivity (0 = least aggressive at detecting silence, 3 = most)")
    parser.add_argument("--max-buffer-seconds", type=float, default=15.0,
                         help="Safety net: close off a chunk after this many seconds even without a pause (long monologue)")
    parser.add_argument("--backend", choices=["auto", "cpu", "gpu"], default="auto",
                         help="auto = use GPU if available, else CPU (default)")
    parser.add_argument("--language", default="de")
    parser.add_argument("--stale-after", type=float, default=16.0,
                         help="Recording is considered finished after this many seconds without file growth")
    parser.add_argument("--out", type=Path, help="Log file (default: prefix with .dictate.txt)")
    parser.add_argument("--keep-audio", action="store_true",
                         help="Don't delete segment files (default: delete each finished segment right after transcribing it)")
    args = parser.parse_args()

    out_path = args.out or args.prefix.with_suffix(".dictate.txt")
    hotwords = _hotwords.load_hotword_string()
    transcribe = load_backend(args.backend, args.language, hotwords)
    vad = webrtcvad.Vad(args.vad_aggressiveness)

    index = 0
    current = segment_path(args.prefix, index)
    wait_for_segment(current)
    data_offset = find_data_offset(current)

    processed_frames = 0  # start of the still-unfinished chunk
    scanned_frames = 0    # how far VAD has classified so far
    silence_run = 0       # consecutive silent frames since the last speech
    had_speech = False    # whether any speech occurred since processed_frames

    last_size = -1
    last_growth_time = time.time()

    pause_frames_needed = max(1, round(args.pause_seconds * 1000 / VAD_FRAME_MS))
    max_buffer_frames = int(args.max_buffer_seconds * SAMPLE_RATE)

    session_texts: list[str] = []

    c.info(f"Dictation running (pause detection, ~{args.pause_seconds:.1f}s of silence closes off a chunk). Focus your target window now.")
    c.info("Typing happens WITHOUT an automatic Enter -- you confirm/run it yourself.\n")

    window_before = active_window_title()
    c.state("listening")

    with out_path.open("w", encoding="utf-8") as out_file:
        while True:
            time.sleep(POLL_SECONDS)

            next_segment = segment_path(args.prefix, index + 1)
            segment_finished = next_segment.exists()

            try:
                cur_size = current.stat().st_size
            except FileNotFoundError:
                break  # segment vanished -- recording was apparently terminated hard

            if cur_size != last_size:
                last_size = cur_size
                last_growth_time = time.time()
                recording_stale = False
            else:
                recording_stale = (time.time() - last_growth_time) > args.stale_after

            if segment_finished or recording_stale:
                # This segment isn't growing anymore -- read to the end, no VAD pause
                # needed since nothing more will ever be appended.
                total_frames = available_frames(current, data_offset)
                text = process_and_type(current, data_offset, processed_frames, total_frames,
                                         transcribe, out_file, window_before)
                if text:
                    session_texts.append(text)
                if not args.keep_audio:
                    current.unlink(missing_ok=True)

                if not segment_finished:
                    c.info("No new data anymore -- recording appears to have ended.")
                    break

                index += 1
                current = next_segment
                wait_for_segment(current)
                data_offset = find_data_offset(current)
                processed_frames = scanned_frames = silence_run = 0
                had_speech = False
                window_before = active_window_title()
                c.state("listening")
                continue

            # Read and classify newly available full VAD frames
            total_frames = available_frames(current, data_offset)
            new_frame_count = (total_frames - scanned_frames) // VAD_FRAME_SAMPLES
            if new_frame_count > 0:
                chunk_end = scanned_frames + new_frame_count * VAD_FRAME_SAMPLES
                raw = read_frame_range_bytes(current, data_offset, scanned_frames, chunk_end)
                for i in range(new_frame_count):
                    frame = raw[i * VAD_FRAME_BYTES:(i + 1) * VAD_FRAME_BYTES]
                    if vad.is_speech(frame, SAMPLE_RATE):
                        had_speech = True
                        silence_run = 0
                    else:
                        silence_run += 1
                scanned_frames = chunk_end

            pause_hit = had_speech and silence_run >= pause_frames_needed
            overflow = (scanned_frames - processed_frames) >= max_buffer_frames

            if pause_hit or overflow:
                text = process_and_type(current, data_offset, processed_frames, scanned_frames,
                                         transcribe, out_file, window_before)
                if text:
                    session_texts.append(text)
                processed_frames = scanned_frames
                had_speech = False
                silence_run = 0
                window_before = active_window_title()
                c.state("listening")

    c.state("finished")
    c.ok("Dictation finished.")
    review_hotword_candidates(session_texts)


if __name__ == "__main__":
    main()
