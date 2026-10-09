using System.Text.RegularExpressions;

using Spectre.Console;
using Spectre.Console.Rendering;

using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Ui.Console;

/// <summary>
/// The header/transcript/footer three-row layout and its live-refresh loop, shared by
/// <see cref="DictationView"/> and <see cref="LiveTranscriptView"/> -- factored out after
/// the two started drifting from each other in small, easy-to-miss ways (the startup-crash
/// bug fixed on 2026-10-08 was exactly this kind of thing, in a different file). Only the
/// header's content differs per mode (different phases, different icons); that part stays
/// mode-specific and gets passed in rather than pulled in here.
/// </summary>
internal static class LiveLayoutRenderer
{
    internal static Layout Build(IRenderable header, IReadOnlyCollection<TranscriptEntry> recentLines, string emptyTranscriptMessage, IRenderable footer)
    {
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(3),
                new Layout("Transcript"),
                new Layout("Footer").Size(1));

        layout["Header"].Update(header);
        layout["Transcript"].Update(BuildTranscript(recentLines, emptyTranscriptMessage));
        layout["Footer"].Update(footer);
        return layout;
    }

    internal static async Task RunAsync(Func<Layout> buildLayout, Func<bool> stopRequested, Func<bool> sessionEnded, CancellationToken cancellationToken, TimeSpan refreshInterval)
    {
        await AnsiConsole.Live(buildLayout())
            .StartAsync(async ctx =>
            {
                while (!stopRequested() && !sessionEnded() && !cancellationToken.IsCancellationRequested)
                {
                    ctx.UpdateTarget(buildLayout());
                    ctx.Refresh();
                    await Task.Delay(refreshInterval, cancellationToken).ConfigureAwait(false);
                }

                ctx.UpdateTarget(buildLayout());
                ctx.Refresh();
            })
            .ConfigureAwait(false);
    }

    // Matches both engine line formats this renders: dictate.py's "HH:MM:SS [window] text"
    // and transcribe_live.py's plain "HH:MM:SS text" (no window group). Group 2 is absent
    // for the second form.
    private static readonly Regex MetaLinePattern = new(
        @"^(\d{2}:\d{2}:\d{2})(?:\s+\[([^\]]*)\])?\s+(.*)$", RegexOptions.Compiled);

    private static IRenderable BuildTranscript(IReadOnlyCollection<TranscriptEntry> lines, string emptyMessage)
    {
        if (lines.Count == 0)
        {
            return new Panel(new Markup($"[grey]{emptyMessage}[/]")).Expand();
        }

        // Each entry renders as a muted "when/where" line followed by the spoken text on
        // its own line, instead of one long run-together line -- reported live as hard to
        // read, with no visual break between the timestamp/window metadata and the actual
        // words (2026-10-09).
        var renderables = new List<IRenderable>();
        bool first = true;
        foreach (TranscriptEntry entry in lines)
        {
            if (!first)
            {
                renderables.Add(new Markup(string.Empty));
            }

            first = false;

            Match match = MetaLinePattern.Match(entry.Text);
            if (match.Success)
            {
                string time = match.Groups[1].Value;
                string meta = match.Groups[2].Success ? $"{time}  ·  {match.Groups[2].Value}" : time;
                renderables.Add(new Markup($"[grey]{meta.EscapeMarkup()}[/]"));
                renderables.Add(new Markup($"[bold]{match.Groups[3].Value.EscapeMarkup()}[/]"));
            }
            else
            {
                // Doesn't match the expected "timestamp [window] text"/"timestamp text"
                // shape -- the engine's line format changed, or this is some other kind of
                // line entirely. Shown as-is rather than silently dropped or crashing on an
                // unmatched group.
                renderables.Add(new Markup(entry.Text.EscapeMarkup()));
            }
        }

        return new Panel(new Rows(renderables)).Expand();
    }
}
