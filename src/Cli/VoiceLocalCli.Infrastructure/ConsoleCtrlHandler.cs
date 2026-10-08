using System.Runtime.InteropServices;

namespace VoiceLocalCli.Infrastructure;

/// <summary>
/// Registers a handler for console control events via <c>SetConsoleCtrlHandler</c> -- the
/// graceful-case layer of orphan prevention. Covers the user closing the console window
/// via the X button (<c>CTRL_CLOSE_EVENT</c>) and Ctrl+C/Ctrl+Break, running the supplied
/// callback synchronously before Windows terminates the process (it allows roughly 5-10
/// seconds before killing the process regardless, per Microsoft's documentation on
/// <c>HandlerRoutine</c>).
///
/// This does NOT cover a hard kill (Task Manager "End Task", a crash, <c>taskkill /F</c>)
/// -- no handler runs in that case at all. <see cref="ProcessJobObject"/> is the separate,
/// unconditional guarantee for that case.
/// </summary>
public static class ConsoleCtrlHandler
{
    // Keeps the delegate alive for the process lifetime -- if it were garbage collected,
    // the native callback pointer Windows holds would become invalid.
    private static PHandlerRoutine? _handler;

    internal enum CtrlType : uint
    {
        CtrlCEvent = 0,
        CtrlBreakEvent = 1,
        CtrlCloseEvent = 2,
        CtrlLogoffEvent = 5,
        CtrlShutdownEvent = 6,
    }

    /// <summary>
    /// Registers <paramref name="onStopRequested"/> to run on any of the control events
    /// above. The callback must complete quickly and synchronously (it runs on a thread
    /// Windows creates specifically for this, inside the termination window) -- it should
    /// block on the stop sequence's own bounded timeouts, not fire-and-forget it.
    /// </summary>
    public static void Register(Action onStopRequested)
    {
        _handler = ctrlType =>
        {
            onStopRequested();
            return false; // let the next handler (eventually the default) also run
        };

        if (!SetConsoleCtrlHandler(_handler, add: true))
        {
            throw new InvalidOperationException($"SetConsoleCtrlHandler failed (Win32 error {Marshal.GetLastWin32Error()}).");
        }
    }

    private delegate bool PHandlerRoutine(CtrlType ctrlType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleCtrlHandler(PHandlerRoutine handlerRoutine, [MarshalAs(UnmanagedType.Bool)] bool add);
}
