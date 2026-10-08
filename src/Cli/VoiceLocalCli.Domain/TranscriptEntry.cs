namespace VoiceLocalCli.Domain;

/// <summary>One line recognized during a session, for the scrolling transcript view.</summary>
/// <param name="Timestamp">When the entry was received by the orchestrator (not the
/// engine's own timestamp, which is embedded in <paramref name="Text"/> for Dictate mode
/// per the engine's current log format).</param>
/// <param name="Text">The raw transcript line as emitted by the engine on stdout.</param>
public sealed record TranscriptEntry(DateTimeOffset Timestamp, string Text);
