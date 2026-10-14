using System.Text.RegularExpressions;

namespace VoiceLocalCli.Application.UseCases;

/// <summary>
/// Strips the ANSI color codes src/engine/_console.py's info()/ok()/warn()/err() wrap their
/// messages in, so the UI can show the engine's own stderr prose as plain status text
/// instead of raw escape sequences. Pure function, mirroring EngineLineClassifier's split
/// between parsing (here) and rendering (the UI layer) -- lets a session surface what the
/// engine is doing before its first stdout @@STATE: event (e.g. "Loading GPU pipeline
/// (OpenVINO, large-v3-turbo) ...") instead of a silently ticking clock, which is otherwise
/// indistinguishable from a real hang (reported live on 2026-10-14).
/// </summary>
public static class EngineStatusLineFormatter
{
    private static readonly Regex AnsiCodePattern = new(@"\x1B\[[0-9;]*m", RegexOptions.Compiled);

    /// <returns>The line with ANSI codes stripped and whitespace trimmed, or null if
    /// nothing's left (a blank line the engine printed for spacing).</returns>
    public static string? Format(string rawLine)
    {
        ArgumentNullException.ThrowIfNull(rawLine);
        string stripped = AnsiCodePattern.Replace(rawLine, string.Empty).Trim();
        return stripped.Length == 0 ? null : stripped;
    }
}
