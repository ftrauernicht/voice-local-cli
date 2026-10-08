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

## A real bug this theme already caused once

`Program.cs` originally built the Figlet banner's color with
`Color.FromInt32(0x58A6FF)` -- `FromInt32` takes a legacy **indexed** terminal color
number (0-255), not a packed RGB value, so `0x58A6FF` (5,799,935) threw
`InvalidOperationException: Color number must be between 0 and 255` on the very first
line of `Main`, before the app could show anything at all. `dotnet build`/`dotnet test`
both stayed green through this, because nothing exercises `Program.cs`'s top-level
statements -- found only by actually running the app, not by the test suite. Fixed to
`new Color(0x58, 0xA6, 0xFF)` (the same three-byte-RGB constructor `DictationView.cs`
was already using correctly). If you add a new color anywhere in this project, use that
constructor, not `Color.FromInt32`.
