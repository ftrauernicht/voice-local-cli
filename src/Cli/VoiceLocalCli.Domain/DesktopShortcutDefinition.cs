namespace VoiceLocalCli.Domain;

/// <summary>
/// Everything needed to create one Windows desktop shortcut (<c>.lnk</c>) for one
/// <see cref="OrchestratorMode"/>: where it should be written, what it should launch, and
/// which icon it should show. Pure data -- building the list of these is a pure function
/// (DesktopShortcutPlan, in the Application project); only writing the actual <c>.lnk</c>
/// file touches the real OS (via the Application project's IShortcutWriter port).
/// </summary>
public sealed record DesktopShortcutDefinition(
    OrchestratorMode Mode,
    string ShortcutPath,
    string TargetPath,
    string Arguments,
    string WorkingDirectory,
    string IconPath,
    string Description);
