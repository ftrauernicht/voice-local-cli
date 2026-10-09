using VoiceLocalCli.Application.Ports;
using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.UseCases;

/// <summary>
/// Creates (or overwrites) the three desktop shortcuts from <see cref="DesktopShortcutPlan"/>,
/// via <see cref="IShortcutWriter"/> -- the one point where this touches the real
/// filesystem/COM API, kept out of <see cref="DesktopShortcutPlan"/> itself so the planning
/// logic stays unit-testable without writing anything.
/// </summary>
public sealed class CreateDesktopShortcutsUseCase(IShortcutWriter shortcutWriter)
{
    private readonly IShortcutWriter _shortcutWriter = shortcutWriter;

    public IReadOnlyList<DesktopShortcutDefinition> Run(DesktopShortcutLaunchTarget launchTarget, string desktopDirectory, string iconsDirectory)
    {
        IReadOnlyList<DesktopShortcutDefinition> shortcuts =
            DesktopShortcutPlan.BuildAll(launchTarget, desktopDirectory, iconsDirectory);

        foreach (DesktopShortcutDefinition shortcut in shortcuts)
        {
            _shortcutWriter.Create(shortcut);
        }

        return shortcuts;
    }
}
