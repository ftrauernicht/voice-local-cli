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

    private static IRenderable BuildTranscript(IReadOnlyCollection<TranscriptEntry> lines, string emptyMessage)
    {
        if (lines.Count == 0)
        {
            return new Panel(new Markup($"[grey]{emptyMessage}[/]")).Expand();
        }

        var rows = new Rows(lines.Select(entry => new Markup(entry.Text.EscapeMarkup())));
        return new Panel(rows).Expand();
    }
}
