using System.Collections.Concurrent;

using Spectre.Console;
using Spectre.Console.Rendering;

using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Ui.Console;

/// <summary>
/// The running-session screen: a header showing the current phase with a spinner, a
/// scrolling window of recent transcript lines, and a footer naming the stop key. Uses
/// <see cref="AnsiConsole.Live(IRenderable)"/> with a <see cref="Layout"/> rather than
/// <c>Status</c>, which can only show a single spinner+text line and has no room for the
/// scrolling transcript.
/// </summary>
internal sealed class DictationView
{
    private const int MaxVisibleLines = 12;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(150);

    // GitHub Dark-inspired accents, applied sparingly (foreground only, never a
    // background fill) -- see docs/UI_THEME.md for the rationale and the exact values,
    // which are a judgment call on an underspecified "GitHub-inspired" request and
    // deliberately centralized here so revising them later is a one-file change.
    //
    // Plain hex strings, not Style/Color objects: interpolating a Color's own ToString()
    // into markup (e.g. "[{color}]...[/]") produces "(RGB=88,166,255)", which isn't valid
    // Spectre markup syntax and throws at render time -- found by actually running Live
    // mode end to end on 2026-10-08, not caught by any unit test (nothing renders real
    // Spectre markup in this project's test suite). "[#58A6FF]...[/]" is the correct
    // syntax, so the hex string is what belongs directly in the markup.
    private const string AccentColor = "#58A6FF";
    private const string MutedColor = "#8B949E";
    private const string TypingColor = "#3FB950";
    private const string FocusMismatchColor = "#D29922";

    private readonly ConcurrentQueue<TranscriptEntry> _recentLines = new();
    private DictationPhase _phase = DictationPhase.Unknown;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;

    internal void OnPhaseChanged(DictationPhase phase) => _phase = phase;

    internal void OnTranscriptReceived(TranscriptEntry entry)
    {
        _recentLines.Enqueue(entry);
        while (_recentLines.Count > MaxVisibleLines)
        {
            _recentLines.TryDequeue(out _);
        }
    }

    /// <summary>Runs the live view until <paramref name="stopRequested"/> signals true
    /// (the user pressed the stop key) or <paramref name="sessionEnded"/> signals true
    /// (the engine finished on its own, e.g. the recording stopped externally).</summary>
    internal Task RunAsync(Func<bool> stopRequested, Func<bool> sessionEnded, CancellationToken cancellationToken) =>
        LiveLayoutRenderer.RunAsync(BuildLayout, stopRequested, sessionEnded, cancellationToken, RefreshInterval);

    private Layout BuildLayout() =>
        LiveLayoutRenderer.Build(BuildHeader(), [.. _recentLines], "Nothing transcribed yet -- start speaking.", BuildFooter());

    private IRenderable BuildHeader()
    {
        (string label, string color, string icon) = _phase switch
        {
            DictationPhase.Listening => ("Listening", AccentColor, "\U0001F442"),
            DictationPhase.Transcribing => ("Transcribing", AccentColor, "✍"),
            DictationPhase.Typing => ("Typing", TypingColor, "⌨"),
            DictationPhase.FocusMismatch => ("Focus changed -- not typed", FocusMismatchColor, "⚠"),
            DictationPhase.Finished => ("Finished", MutedColor, "✓"),
            _ => ("Starting...", MutedColor, "…"),
        };

        TimeSpan elapsed = DateTimeOffset.Now - _startedAt;
        var markup = new Markup($"{icon} [{color}]{label.EscapeMarkup()}[/]  [grey]{elapsed:hh\\:mm\\:ss}[/]");
        return new Panel(markup).Header("voice-local-cli -- Dictate").Expand();
    }

    private static IRenderable BuildFooter() =>
        new Markup("[grey]Press[/] [bold]Esc[/] [grey]to stop.[/]");
}
