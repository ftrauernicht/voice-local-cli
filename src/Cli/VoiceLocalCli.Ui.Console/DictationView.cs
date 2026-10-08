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
    private static readonly Style AccentStyle = new(new Color(0x58, 0xA6, 0xFF));
    private static readonly Style MutedStyle = new(new Color(0x8B, 0x94, 0x9E));
    private static readonly Style TypingStyle = new(new Color(0x3F, 0xB9, 0x50));
    private static readonly Style FocusMismatchStyle = new(new Color(0xD2, 0x99, 0x22));

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
    internal async Task RunAsync(Func<bool> stopRequested, Func<bool> sessionEnded, CancellationToken cancellationToken)
    {
        await AnsiConsole.Live(BuildLayout())
            .StartAsync(async ctx =>
            {
                while (!stopRequested() && !sessionEnded() && !cancellationToken.IsCancellationRequested)
                {
                    ctx.UpdateTarget(BuildLayout());
                    ctx.Refresh();
                    await Task.Delay(RefreshInterval, cancellationToken).ConfigureAwait(false);
                }

                ctx.UpdateTarget(BuildLayout());
                ctx.Refresh();
            })
            .ConfigureAwait(false);
    }

    private Layout BuildLayout()
    {
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(3),
                new Layout("Transcript"),
                new Layout("Footer").Size(1));

        layout["Header"].Update(BuildHeader());
        layout["Transcript"].Update(BuildTranscript());
        layout["Footer"].Update(BuildFooter());
        return layout;
    }

    private IRenderable BuildHeader()
    {
        (string label, Style style, string icon) = _phase switch
        {
            DictationPhase.Listening => ("Listening", AccentStyle, "\U0001F442"),
            DictationPhase.Transcribing => ("Transcribing", AccentStyle, "✍"),
            DictationPhase.Typing => ("Typing", TypingStyle, "⌨"),
            DictationPhase.FocusMismatch => ("Focus changed -- not typed", FocusMismatchStyle, "⚠"),
            DictationPhase.Finished => ("Finished", MutedStyle, "✓"),
            _ => ("Starting...", MutedStyle, "…"),
        };

        TimeSpan elapsed = DateTimeOffset.Now - _startedAt;
        var markup = new Markup($"{icon} [{style.Foreground}]{label.EscapeMarkup()}[/]  [grey]{elapsed:hh\\:mm\\:ss}[/]");
        return new Panel(markup).Header("voice-local-cli -- Dictate").Expand();
    }

    private IRenderable BuildTranscript()
    {
        TranscriptEntry[] lines = [.. _recentLines];
        if (lines.Length == 0)
        {
            return new Panel(new Markup("[grey]Nothing transcribed yet -- start speaking.[/]")).Expand();
        }

        var rows = new Rows(lines.Select(entry => new Markup(entry.Text.EscapeMarkup())));
        return new Panel(rows).Expand();
    }

    private static IRenderable BuildFooter() =>
        new Markup("[grey]Press[/] [bold]Esc[/] [grey]to stop.[/]");
}
