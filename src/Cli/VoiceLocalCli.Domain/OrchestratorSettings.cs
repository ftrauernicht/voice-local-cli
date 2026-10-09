namespace VoiceLocalCli.Domain;

/// <summary>
/// Persisted, machine-specific orchestrator settings -- not part of the repo, stored under
/// <c>%LOCALAPPDATA%\voice-local-cli\</c> (see <c>Application.Ports.ISettingsStore</c>).
/// </summary>
/// <param name="RecordingsDirectory">Where recordings/transcripts are written. Null means
/// the default (the repository's own <c>recordings\</c> folder).</param>
public sealed record OrchestratorSettings(string? RecordingsDirectory = null);
