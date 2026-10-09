namespace VoiceLocalCli.Domain;

/// <summary>A newer release found via <c>Application.Ports.IUpdateChecker</c>. Pure data --
/// applying it is a separate, explicit step the user triggers, never automatic (see
/// docs/adr/0004-velopack-for-auto-updates.md: never mid-dictation-session).</summary>
public sealed record AvailableUpdate(string Version, string? ReleaseNotes);
