"""Makes src/engine's flat modules importable as `import _console`, `import dictate`,
etc. -- the engine isn't a nested package (see pyproject.toml's py-modules list), so
tests need its directory on sys.path, the same way running a script directly from that
directory already provides it."""
import sys
from pathlib import Path

ENGINE_DIR = Path(__file__).resolve().parents[2] / "src" / "engine"
if str(ENGINE_DIR) not in sys.path:
    sys.path.insert(0, str(ENGINE_DIR))
