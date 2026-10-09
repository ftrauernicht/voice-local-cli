namespace VoiceLocalCli.Domain;

/// <summary>
/// What a desktop shortcut should launch and how. Resolved by the caller -- `dotnet run`
/// against a dev checkout, or the installed exe directly -- and kept opaque here so
/// <c>DesktopShortcutPlan</c> stays free of any dotnet-SDK/Velopack-specific knowledge.
/// </summary>
/// <param name="TargetPath">The executable a shortcut launches (`dotnet.exe` in dev mode,
/// the installed `VoiceLocalCli.exe` otherwise).</param>
/// <param name="ArgumentsPrefix">Anything that must precede `--mode &lt;x&gt;` on the command
/// line (dev mode: `run --project "..." --`; installed mode: empty).</param>
/// <param name="WorkingDirectory">The shortcut's working directory.</param>
public sealed record DesktopShortcutLaunchTarget(string TargetPath, string ArgumentsPrefix, string WorkingDirectory);
