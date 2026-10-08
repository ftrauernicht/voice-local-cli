namespace VoiceLocalCli.Application.Ports;

/// <summary>
/// A running child process, abstracted so Application-layer logic (the session lifecycle,
/// the stop sequence) can be unit-tested against a fake instead of a real OS process. The
/// real adapter (Infrastructure) wraps <see cref="System.Diagnostics.Process"/> with
/// redirected stdio and no visible window.
/// </summary>
public interface IChildProcess
{
    int Id { get; }

    bool HasExited { get; }

    /// <summary>Raised once per line received on the process's standard output.</summary>
    event Action<string>? OutputLineReceived;

    /// <summary>Raised once per line received on the process's standard error.</summary>
    event Action<string>? ErrorLineReceived;

    /// <summary>
    /// Writes text to the process's standard input without appending a newline or closing
    /// the stream -- ffmpeg's Windows control-key handling (see docs/MANUAL_VERIFICATION.md
    /// for the spike that confirmed this) reads a raw 'q' byte from a piped stdin the same
    /// way it reads a keypress from a real console; closing the stream sends EOF instead,
    /// a different signal.
    /// </summary>
    Task WriteStandardInputAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Waits up to <paramref name="timeout"/> for the process to exit on its own.
    /// Returns true if it exited within that time.</summary>
    Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Hard-kills the process and, where the adapter supports it, its full
    /// process tree. The fallback path when a graceful stop doesn't complete in time.</summary>
    void Kill();
}
