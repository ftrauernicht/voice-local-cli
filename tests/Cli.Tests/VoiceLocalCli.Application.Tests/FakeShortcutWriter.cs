using VoiceLocalCli.Application.Ports;
using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.Tests;

/// <summary>Records every <see cref="Create"/> call instead of touching the real
/// filesystem/COM API -- lets a test assert exactly which shortcuts
/// <see cref="UseCases.CreateDesktopShortcutsUseCase"/> asked to be written.</summary>
internal sealed class FakeShortcutWriter : IShortcutWriter
{
    internal List<DesktopShortcutDefinition> CreateCalls { get; } = [];

    public void Create(DesktopShortcutDefinition shortcut) => CreateCalls.Add(shortcut);
}
