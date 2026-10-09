using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.UseCases;

/// <summary>
/// Builds the three <see cref="DesktopShortcutDefinition"/>s (one per
/// <see cref="OrchestratorMode"/>) for a given <see cref="DesktopShortcutLaunchTarget"/>.
/// Pure function: no file is written here, so this is fully unit-testable, and it has no
/// idea whether the target is a dev checkout or an installed copy -- that decision is the
/// caller's (see Program.cs's `CreateShortcuts`).
/// </summary>
public static class DesktopShortcutPlan
{
    public static IReadOnlyList<DesktopShortcutDefinition> BuildAll(
        DesktopShortcutLaunchTarget launchTarget, string desktopDirectory, string iconsDirectory)
    {
        ArgumentNullException.ThrowIfNull(launchTarget);
        ArgumentException.ThrowIfNullOrWhiteSpace(launchTarget.TargetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(launchTarget.WorkingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(iconsDirectory);

        return
        [
            Build(OrchestratorMode.Dictate, "Hands-free local dictation into whatever window has focus."),
            Build(OrchestratorMode.Live, "Live call transcript while the call is in progress."),
            Build(OrchestratorMode.Call, "Full call recording with speaker diarization after the call ends."),
        ];

        DesktopShortcutDefinition Build(OrchestratorMode mode, string description)
        {
            string modeArgument = mode.ToString().ToLowerInvariant();
            string arguments = string.IsNullOrEmpty(launchTarget.ArgumentsPrefix)
                ? $"--mode {modeArgument}"
                : $"{launchTarget.ArgumentsPrefix} --mode {modeArgument}";

            return new DesktopShortcutDefinition(
                Mode: mode,
                ShortcutPath: Path.Combine(desktopDirectory, $"voice-local-cli - {mode}.lnk"),
                TargetPath: launchTarget.TargetPath,
                Arguments: arguments,
                WorkingDirectory: launchTarget.WorkingDirectory,
                IconPath: Path.Combine(iconsDirectory, $"{modeArgument}.ico"),
                Description: description);
        }
    }
}
