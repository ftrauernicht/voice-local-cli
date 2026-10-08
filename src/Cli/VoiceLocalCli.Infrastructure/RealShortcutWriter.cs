using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using VoiceLocalCli.Application.Ports;
using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Infrastructure;

/// <summary>
/// The real <see cref="IShortcutWriter"/> adapter: creates a Windows <c>.lnk</c> file via
/// the <c>WScript.Shell</c> COM component (late-bound through <c>dynamic</c> -- no
/// <c>Windows Script Host Object Model</c> type-library reference needed for three
/// property sets and a <c>Save()</c> call).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RealShortcutWriter : IShortcutWriter
{
    public void Create(DesktopShortcutDefinition shortcut)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcut.ShortcutPath)!);

        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("The WScript.Shell COM component is not available on this machine.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(shortcut.ShortcutPath);
            try
            {
                link.TargetPath = shortcut.TargetPath;
                link.Arguments = shortcut.Arguments;
                link.WorkingDirectory = shortcut.WorkingDirectory;
                link.Description = shortcut.Description;
                if (File.Exists(shortcut.IconPath))
                {
                    link.IconLocation = shortcut.IconPath;
                }

                link.Save();
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(shell);
        }
    }
}
