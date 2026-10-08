using System.Collections.Concurrent;

using Spectre.Console;
using Spectre.Console.Rendering;

using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Ui.Console;

/// <summary>
/// The running-session screen for Live mode: a header showing the current phase, a
/// scrolling window of recent transcript lines, and a footer naming the stop key. Phase is
/// the engine's raw <c>@@STATE:</c> event name (see <c>LiveTranscriptSession.Phase</c>'s
/// own doc comment for why this, unlike Dictate, has no typed phase enum). Shares its
/// layout and refresh loop with <see cref="DictationView"/> via
/// <see cref="LiveLayoutRenderer"/>; only the header content differs.
/// </summary>
internal sealed class LiveTranscriptView
{
    private const int MaxVisibleLines = 12;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(150);

    // Same palette as DictationView -- see docs/UI_THEME.md. Plain hex strings for markup,
    // not Style/Color objects -- see DictationView's own comment for why.
    private const string AccentColor = "#58A6FF";
    private const string MutedColor = "#8B949E";

    private readonly ConcurrentQueue<TranscriptEntry> _recentLines = new();
    private string _phase = "starting";
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;

    internal void OnPhaseChanged(string phase) => _phase = phase;

    internal void OnTranscriptReceived(TranscriptEntry entry)
    {
        _recentLines.Enqueue(entry);
        while (_recentLines.Count > MaxVisibleLines)
        {
            _recentLines.TryDequeue(out _);
        }
    }

    internal Task RunAsync(Func<bool> stopRequested, Func<bool> sessionEnded, CancellationToken cancellationToken) =>
        LiveLayoutRenderer.RunAsync(BuildLayout, stopRequested, sessionEnded, cancellationToken, RefreshInterval);

    private Layout BuildLayout() =>
        LiveLayoutRenderer.Build(BuildHeader(), [.. _recentLines], "Nothing transcribed yet -- stay on the call.", BuildFooter());

    private IRenderable BuildHeader()
    {
        (string label, string color, string icon) = _phase switch
        {
            "recording" => ("Recording", AccentColor, "\U0001F442"),
            "transcribing_chunk" => ("Transcribing", AccentColor, "✍"),
            "finished" => ("Finished", MutedColor, "✓"),
            _ => ("Starting...", MutedColor, "…"),
        };

        TimeSpan elapsed = DateTimeOffset.Now - _startedAt;
        var markup = new Markup($"{icon} [{color}]{label.EscapeMarkup()}[/]  [grey]{elapsed:hh\\:mm\\:ss}[/]");
        return new Panel(markup).Header("voice-local-cli -- Live").Expand();
    }

    private static IRenderable BuildFooter() =>
        new Markup("[grey]Press[/] [bold]Esc[/] [grey]to stop.[/]");
}
