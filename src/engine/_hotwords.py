"""Hotwords: a local list of names/jargon that should be recognized preferentially during
transcription (e.g. colleague names, tool names like "wmux") -- both `faster-whisper` (CPU)
and OpenVINO GenAI's `WhisperPipeline.generate()` (GPU) accept a `hotwords` parameter with
identical effect, no CPU/GPU trade-off.

The list itself (`hotwords.local.json`) isn't maintained by hand -- it grows through a
prompt: `dictate.py` counts, at the end of every dictation session, which words (excluding
common German filler words) came up how often, and above a threshold asks whether a
recurring word should be added to the list (`hotwords_candidates.local.json` keeps the
bookkeeping for that). No silent, unattended change -- only an explicit confirmation adds
anything.

Both files are git-ignored (like devices.local.json): they can contain real names and
client-specific jargon that have no place in a shared repo.
"""
import json
import re
import time
from pathlib import Path

import _console as c

HOTWORDS_PATH = Path(__file__).parent / "hotwords.local.json"
CANDIDATES_PATH = Path(__file__).parent / "hotwords_candidates.local.json"

MIN_WORD_LENGTH = 3
DEFAULT_THRESHOLD = 3

# Common German filler/function words -- without this filter, "und"/"ist"/"der"/... would
# dominate every frequency count instead of real, recurring jargon. This list is itself
# German vocabulary (the thing being filtered), so it stays German regardless of the
# project's English-language convention.
STOPWORDS_DE = frozenset("""
aber alle allem allen aller alles als also am an ander andere anderem anderen anderer
anderes anderm andern anders auch auf aus bei bin bis bist da damit dann das dasselbe
dazu dein deine deinem deinen deiner deines dem demselben den denn denselben der derer
derselbe derselben des desselben dessen dich die dies diese dieselbe dieselben diesem
diesen dieser dieses dir doch dort du durch ein eine einem einen einer eines einig
einige einigem einigen einiger einiges einmal er euch euer eure eurem euren eurer eures
für gegen gewesen hab habe haben hat hatte hatten hier hin hinter ich ihm ihn ihnen ihr
ihre ihrem ihren ihrer ihres im immer in indem ist ja jede jedem jeden jeder jedes jene
jenem jenen jener jenes jetzt kann kein keine keinem keinen keiner keines können könnte
machen man manche manchem manchen mancher manches mein meine meinem meinen meiner meines
mich mir mit muss musste nach nicht nichts noch nun nur ob oder ohne sehr sein seine
seinem seinen seiner seines selbst sich sie sind so solche solchem solchen solcher
solches soll sollte sondern sonst über um und uns unse unser unsere unserem unseren
unter viel vom von vor war waren warst was weg weil weiter weitere wenn werde werden
wie wieder will wir wird wirst wo wollen wollte während würde würden zu zum zur zwar
zwischen schon dabei dafür dadurch dagegen daher damals danach daran darauf daraus
darf darfst darin darüber darum davon davor dazwischen dein deinige derjenige diejenige
dasjenige derer dergestalt derlei desto diesseits dort dorther dorthin droben droben
drei drunter drüber eben ebenso ehrlich eigen eigene eigenen eigenes einander einige
einigermaßen einmaleins etwas etwa euretwegen folgende folgendes gar gerade gern geschweige
gleich hallo heraus herein herum hinaus hinein hinten hinterher ihrerseits indessen infolge
innerhalb irgend irgendeine irgendwas irgendwen irgendwer irgendwie irgendwo je jedenfalls
jedermann jedoch jemals jemand jenseits jetzt kannst kaum keinerlei lassen lediglich leicht
leider los mag magst mal manch mehr mehrere meist meiste meisten mochte möchte mögen
möglich mögliche na nachdem nein neun neun neben nein nebenan niemand nie nirgends nirgendwo
nämlich nötigenfalls öfter ohnehin paar rund sechs sieben siebte so solang solche
somit sonstwo soweit sowie sowohl später statt tatsächlich trotzdem tunlichst überall
überallhin überdies übermorgen übrigens unbedingt ungefähr unweit vielen vielleicht vielmals
vielmehr vollends voran vorbei vorgestern vorher vorüber während währenddessen wann
warum weitere weiterhin weiters welche welchem welchen welcher welches wem wen wenig
wenige weniger wenigstens wessen wetten wiederum wieso wieviel wieviele wirklich wohl
wohlgemerkt worden zehn zeitweise ziemlich zugleich zuletzt zumal zunächst zurzeit
zusammen zuviel zuwenig zwecks zwölf
""".split())


def _read_json(path: Path, default: dict) -> dict:
    if not path.exists():
        return default
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:
        c.warn(f"{path.name} unreadable, ignoring it: {exc}")
        return default


def _write_json(path: Path, data: dict) -> None:
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def load_hotword_string() -> str:
    """Returns the current hotwords list as a single space-separated string, the format
    `hotwords=` expects from both faster-whisper and OpenVINO GenAI. Empty string (not an
    error) if no list exists yet -- hotwords is a quality add-on, not a mandatory step like
    device selection."""
    data = _read_json(HOTWORDS_PATH, {"hotwords": []})
    words = data.get("hotwords", [])
    return " ".join(words)


def add_hotword(word: str) -> None:
    data = _read_json(HOTWORDS_PATH, {"hotwords": []})
    words = data.setdefault("hotwords", [])
    if not any(w.lower() == word.lower() for w in words):
        words.append(word)
        _write_json(HOTWORDS_PATH, data)


def tokenize(text: str) -> list[str]:
    """Splits transcribed text into candidate words: letters only, at least
    MIN_WORD_LENGTH characters, no common filler words. Deliberately does NOT use
    capitalization as a signal for proper nouns -- in German, every noun is capitalized,
    not just names, so it wouldn't distinguish "Moritz" from "Tisch" (table)."""
    words = re.findall(r"[A-Za-zÄÖÜäöüß]+", text)
    return [
        w for w in words
        if len(w) >= MIN_WORD_LENGTH and w.lower() not in STOPWORDS_DE
    ]


def update_counts(texts: list[str]) -> None:
    """Counts a dictation session's words into the candidate bookkeeping."""
    if not texts:
        return
    data = _read_json(CANDIDATES_PATH, {"counts": {}, "dismissed": []})
    counts = data.setdefault("counts", {})
    today = time.strftime("%Y-%m-%d")

    for text in texts:
        for word in tokenize(text):
            key = word.lower()
            entry = counts.get(key)
            if entry is None:
                counts[key] = {"count": 1, "display": word, "first_seen": today, "last_seen": today}
            else:
                entry["count"] += 1
                entry["last_seen"] = today

    _write_json(CANDIDATES_PATH, data)


def due_candidates(threshold: int = DEFAULT_THRESHOLD) -> list[tuple[str, dict]]:
    """Words that came up often enough, aren't already in the hotwords list, and weren't
    permanently dismissed -- candidates for the end-of-session prompt."""
    candidates_data = _read_json(CANDIDATES_PATH, {"counts": {}, "dismissed": []})
    counts = candidates_data.get("counts", {})
    dismissed = set(candidates_data.get("dismissed", []))

    hotwords_data = _read_json(HOTWORDS_PATH, {"hotwords": []})
    already_added = {w.lower() for w in hotwords_data.get("hotwords", [])}

    result = [
        (key, entry) for key, entry in counts.items()
        if entry["count"] >= threshold and key not in already_added and key not in dismissed
    ]
    result.sort(key=lambda item: item[1]["count"], reverse=True)
    return result


def dismiss_candidate(key: str) -> None:
    data = _read_json(CANDIDATES_PATH, {"counts": {}, "dismissed": []})
    dismissed = data.setdefault("dismissed", [])
    if key not in dismissed:
        dismissed.append(key)
        _write_json(CANDIDATES_PATH, data)
