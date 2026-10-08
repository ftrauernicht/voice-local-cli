using VoiceLocalCli.Application.Ports;

namespace VoiceLocalCli.Application.UseCases;

/// <summary>
/// The stop sequence shared by every session that owns a child process: try a graceful
/// signal (or just wait, for a process with no signal of its own), then kill if it doesn't
/// exit in time. Factored out of <see cref="DictationSession"/> rather than copied into
/// <see cref="LiveTranscriptSession"/>/<see cref="CallRecordingSession"/> as well --
/// this is the exact mechanism that fixed the real 35-minutes-orphaned-ffmpeg bug (see
/// docs/MANUAL_VERIFICATION.md), and three independently-maintained copies of it would be
/// three independent chances for one of them to quietly drift and reintroduce that bug.
/// </summary>
internal static class ChildProcessStopSequence
{
    /// <summary>Writes <paramref name="stopSignal"/> to the process's stdin (ffmpeg reads a
    /// raw keypress this way, see <see cref="IChildProcess.WriteStandardInputAsync"/>'s own
    /// doc comment), then waits up to <paramref name="timeout"/> before killing it.</summary>
    internal static async Task SignalThenKillIfNeededAsync(
        IChildProcess process, string stopSignal, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (process.HasExited)
        {
            return;
        }

        await process.WriteStandardInputAsync(stopSignal, cancellationToken).ConfigureAwait(false);
        await WaitThenKillIfNeededAsync(process, timeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>For a process with no stop signal of its own (it's expected to notice its
    /// input has gone away and exit by itself): waits up to <paramref name="timeout"/>,
    /// then kills it if it hasn't.</summary>
    internal static async Task WaitThenKillIfNeededAsync(
        IChildProcess process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (process.HasExited)
        {
            return;
        }

        bool exited = await process.WaitForExitAsync(timeout, cancellationToken).ConfigureAwait(false);
        if (!exited)
        {
            process.Kill();
        }
    }
}
