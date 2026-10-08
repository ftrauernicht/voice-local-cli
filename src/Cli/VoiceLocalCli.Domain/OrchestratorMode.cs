namespace VoiceLocalCli.Domain;

/// <summary>
/// The three capabilities the orchestrator's menu offers. Only <see cref="Dictate"/> is
/// wired up to a real session today -- <see cref="Live"/> and <see cref="Call"/> exist so
/// the menu, the CLI's <c>--mode</c> argument, and desktop shortcuts have a shared,
/// exhaustive vocabulary to target, ready for when those two get a real use case behind
/// them (see docs/MANUAL_VERIFICATION.md).
/// </summary>
public enum OrchestratorMode
{
    Dictate,
    Live,
    Call,
}
