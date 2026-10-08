# Console UI theme

The orchestrator's colors are centralized in one place: `DictationView`'s
`AccentStyle`/`MutedStyle`/`TypingStyle`/`FocusMismatchStyle` fields, and the matching
`FigletText` color in `Program.cs`. Revising the palette is a one-file-plus-one-line
change by design.

| Role | Hex | Used for |
|---|---|---|
| Accent | `#58A6FF` | The banner, "Listening"/"Transcribing" header states |
| Muted | `#8B949E` | "Starting...", "Finished", secondary text |
| Typing (success) | `#3FB950` | The "Typing" header state |
| Focus mismatch (warning) | `#D29922` | The "Focus changed -- not typed" header state |

Loosely GitHub Dark-inspired, per the original request -- not a literal port of GitHub's
own palette. This is a judgment call made without a human looking at a real terminal
while building it (see `docs/MANUAL_VERIFICATION.md` item 5): look at it yourself in
Windows Terminal (and separately in legacy `conhost`, if that matters to you) and change
the four `Color(...)` values in `DictationView.cs` directly if it needs adjusting.

## Two real bugs this theme already caused

Both found only by actually running the app end to end on 2026-10-08 -- `dotnet build`
and `dotnet test` stayed green through both, because neither exercises `Program.cs`'s
top-level statements or renders real Spectre markup.

1. **`Program.cs` originally built the Figlet banner's color with
   `Color.FromInt32(0x58A6FF)`.** `FromInt32` takes a legacy **indexed** terminal color
   number (0-255), not a packed RGB value, so `0x58A6FF` (5,799,935) threw
   `InvalidOperationException: Color number must be between 0 and 255` on the very first
   line of `Main`, before the app could show anything at all. Fixed to
   `new Color(0x58, 0xA6, 0xFF)`.
2. **`DictationView`/`LiveTranscriptView` originally built their header markup by
   interpolating a `Style`'s `.Foreground` `Color` directly into the markup string**
   (`$"[{style.Foreground}]..."`). A `Color`'s `ToString()` renders as `(RGB=88,166,255)`,
   which is not valid Spectre markup syntax -- every header render past the first
   ("Starting...") threw `InvalidOperationException: Could not find color or style
   '(RGB=88,166,255)'`, crashing Live and Dictate mode's live view the instant a real
   phase change arrived. Fixed by keeping the four colors as plain hex strings
   (`"#58A6FF"`, etc.) and interpolating those directly -- `[#58A6FF]...[/]` is valid
   markup, a `Color` object's default string form is not.

If you add a new color anywhere in this project: use the `new Color(r, g, b)`
constructor to build one, and use a plain `"#RRGGBB"` hex string (not a `Color`/`Style`
object's interpolated `ToString()`) wherever it needs to go into a markup string.
