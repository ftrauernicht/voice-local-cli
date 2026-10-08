using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.UseCases;

/// <summary>
/// Builds the three <see cref="DesktopShortcutDefinition"/>s (one per
/// <see cref="OrchestratorMode"/>), each launching `dotnet run --project ... -- --mode
/// &lt;x&gt;` from the repository root. Pure function: no file is written here, so this is
/// fully unit-testable. Targets `dotnet run` rather than a published executable because no
/// release pipeline exists yet (see the auto-update research referenced from the README's
/// roadmap) -- these shortcuts only work from a source checkout, the same constraint the
/// Ui.Console project's RepositoryLayout already documents for everything else in this
/// project today.
/// </summary>
public static class DesktopShortcutPlan
{
    public static IReadOnlyList<DesktopShortcutDefinition> BuildAll(
        string repositoryRoot, string desktopDirectory, string dotnetExecutablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dotnetExecutablePath);

        string projectPath = Path.Combine(repositoryRoot, "src", "Cli", "VoiceLocalCli.Ui.Console");
        string iconsDirectory = Path.Combine(projectPath, "Assets", "icons");

        return
        [
            Build(OrchestratorMode.Dictate, "Hands-free local dictation into whatever window has focus."),
            Build(OrchestratorMode.Live, "Live call transcript (coming soon -- see the README's Roadmap)."),
            Build(OrchestratorMode.Call, "Full call recording with speaker diarization (coming soon -- see the README's Roadmap)."),
        ];

        DesktopShortcutDefinition Build(OrchestratorMode mode, string description)
        {
            string modeArgument = mode.ToString().ToLowerInvariant();
            return new DesktopShortcutDefinition(
                Mode: mode,
                ShortcutPath: Path.Combine(desktopDirectory, $"voice-local-cli - {mode}.lnk"),
                TargetPath: dotnetExecutablePath,
                Arguments: $"run --project \"{projectPath}\" -- --mode {modeArgument}",
                WorkingDirectory: repositoryRoot,
                IconPath: Path.Combine(iconsDirectory, $"{modeArgument}.ico"),
                Description: description);
        }
    }
}
