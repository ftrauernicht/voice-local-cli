using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Win32.SafeHandles;

namespace VoiceLocalCli.Infrastructure;

/// <summary>
/// Watches for the Esc keypress by reading the real console input buffer directly via
/// <c>ReadConsoleInput</c> on a <c>CONIN$</c> handle, instead of
/// <see cref="System.Console.KeyAvailable"/>/<see cref="System.Console.ReadKey(bool)"/>.
///
/// <see cref="System.Console"/>'s own key-reading is documented to misbehave under
/// Windows Terminal/ConPTY -- it can report "input redirected" (throwing
/// <see cref="InvalidOperationException"/>) even in a genuinely interactive, focused
/// terminal session. Confirmed as the real cause, not a focus issue: reported live
/// (2026-10-09) with the orchestrator's own console window actually focused while
/// pressing Esc, which ruled out the focus-dependent explanation this class's
/// predecessor assumed. Opening <c>CONIN$</c> directly bypasses whatever heuristic
/// <see cref="System.Console"/> uses to (mis)detect redirection.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ConsoleEscapeWatcher
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x1;
    private const uint FileShareWrite = 0x2;
    private const uint OpenExisting = 3;
    private const ushort KeyEvent = 0x0001;
    private const ushort VkEscape = 0x1B;

    [StructLayout(LayoutKind.Explicit)]
    private struct InputRecord
    {
        [FieldOffset(0)]
        public ushort EventType;

        [FieldOffset(4)]
        public KeyEventRecord KeyEvent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyEventRecord
    {
        public int KeyDown; // Win32 BOOL -- nonzero means pressed, zero means released
        public ushort RepeatCount;
        public ushort VirtualKeyCode;
        public ushort VirtualScanCode;
        public char UnicodeChar;
        public uint ControlKeyState;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, nint securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PeekConsoleInput(SafeFileHandle consoleInput, [Out] InputRecord[] buffer, uint length, out uint eventsRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadConsoleInput(SafeFileHandle consoleInput, [Out] InputRecord[] buffer, uint length, out uint eventsRead);

    /// <summary>Polls for Esc until <paramref name="stopRequested"/> or
    /// <paramref name="sessionEnded"/> is already true, calling <paramref name="onEscape"/>
    /// each time it's pressed. Returns false immediately, without polling, if the real
    /// console input buffer couldn't be opened (no real console attached at all -- a
    /// genuinely non-interactive run, e.g. piped output) -- the caller then has no way to
    /// detect Esc and should fall back to Ctrl+C/window-close only.</summary>
    public static bool Watch(Func<bool> stopRequested, Func<bool> sessionEnded, Action onEscape)
    {
        using SafeFileHandle handle = CreateFile(
            "CONIN$", GenericRead | GenericWrite, FileShareRead | FileShareWrite,
            0, OpenExisting, 0, 0);
        if (handle.IsInvalid)
        {
            return false;
        }

        var buffer = new InputRecord[1];
        while (!stopRequested() && !sessionEnded())
        {
            if (!PeekConsoleInput(handle, buffer, 1, out uint pending))
            {
                // The handle stopped being valid (e.g. the console was closed around us) --
                // nothing more this loop can do.
                return true;
            }

            if (pending == 0)
            {
                Thread.Sleep(50);
                continue;
            }

            if (!ReadConsoleInput(handle, buffer, 1, out _))
            {
                return true;
            }

            if (buffer[0].EventType == KeyEvent &&
                buffer[0].KeyEvent.KeyDown != 0 &&
                buffer[0].KeyEvent.VirtualKeyCode == VkEscape)
            {
                onEscape();
            }

            // Every other record (key-up, other keys, mouse, resize, focus) is read and
            // discarded above along with Esc -- ReadConsoleInput removes whatever it reads
            // from the buffer either way, and nothing else in this view reads console
            // input, so there is nothing to preserve it for.
        }

        return true;
    }
}
