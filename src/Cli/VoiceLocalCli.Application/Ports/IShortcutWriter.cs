using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.Ports;

/// <summary>
/// Writes a real Windows shortcut (<c>.lnk</c>) file. The one port through which
/// Application code touches shortcut creation -- fake this in tests, never the Windows
/// Script Host COM API directly.
/// </summary>
public interface IShortcutWriter
{
    void Create(DesktopShortcutDefinition shortcut);
}
