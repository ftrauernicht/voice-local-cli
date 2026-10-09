# Console UI theme

The orchestrator's colors are centralized in one place: `DictationView`'s
`AccentColor`/`MutedColor`/`TypingColor`/`FocusMismatchColor` constants, and the matching
`FigletText` color in `Program.cs`. Revising the palette is a one-file-plus-one-line
change by design.

| Role | Hex | Used for |
|---|---|---|
| Accent | `#58A6FF` | The banner, "Listening"/"Transcribing" header states |
| Muted | `#8B949E` | "Starting...", "Finished", secondary text |
| Typing (success) | `#3FB950` | The "Typing" header state |
| Focus mismatch (warning) | `#D29922` | The "Focus changed -- not typed" header state |

Loosely GitHub Dark-inspired, not a literal port of GitHub's own palette -- a judgment
call (see `docs/MANUAL_VERIFICATION.md`). Look at it yourself in a real terminal and
change the four `Color(...)` values in `DictationView.cs` directly if it needs adjusting.

## Working with colors in this codebase

- Build a `Color` with the `new Color(r, g, b)` constructor, not `Color.FromInt32(...)`
  (that one takes a legacy indexed terminal color number, 0-255, not a packed RGB value).
- Wherever a color needs to go into a Spectre markup string, use a plain `"#RRGGBB"` hex
  string constant (e.g. `"#58A6FF"`), interpolated directly as `[#58A6FF]...[/]` -- never
  a `Color`/`Style` object's own `ToString()`, which does not produce valid markup syntax.
