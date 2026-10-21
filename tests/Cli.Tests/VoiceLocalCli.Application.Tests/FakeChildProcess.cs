using VoiceLocalCli.Application.Ports;
using VoiceLocalCli.Application.UseCases;

namespace VoiceLocalCli.Application.Tests;

/// <summary>A controllable <see cref="IChildProcess"/> fake: tests drive its exit state
/// and emitted lines directly, so <see cref="DictationSession"/>'s logic (not a real OS
/// process) is what's under test.</summary>
internal sealed class FakeChildProcess : IChildProcess
{
    private readonly List<string> _standardInputWrites = [];

    public bool HasExited { get; set; }

    public bool WasKilled { get; private set; }

    public bool ExitsWithinTimeout { get; set; } = true;

    public IReadOnlyList<string> StandardInputWrites => _standardInputWrites;

    /// <summary>Lines to emit via <see cref="OutputLineReceived"/> right before reporting
    /// as exited, simulating a real process (like transcribe.py) that prints a final
    /// status line and then exits almost immediately after -- without this, a test waiting
    /// on <see cref="WaitForExitAsync(TimeSpan, CancellationToken)"/> for a process whose exit carries meaning (e.g. its
    /// last <c>@@STATE:</c> event) would have no way to inject that line at the right
    /// moment, since the fake's exit is already-synchronously-completed.</summary>
    public List<string> OutputLinesToEmitBeforeExit { get; } = [];

    /// <summary>Lines to emit via <see cref="ErrorLineReceived"/> right before reporting as
    /// exited -- the stderr counterpart of <see cref="OutputLinesToEmitBeforeExit"/>, for
    /// exercising a session's engine-stderr handling (e.g. Call-mode progress/error prose).</summary>
    public List<string> ErrorLinesToEmitBeforeExit { get; } = [];

    public int Id => 4242;

    public event Action<string>? OutputLineReceived;

    public event Action<string>? ErrorLineReceived;

    public Task WriteStandardInputAsync(string text, CancellationToken cancellationToken = default)
    {
        _standardInputWrites.Add(text);
        return Task.CompletedTask;
    }

    public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (ExitsWithinTimeout)
        {
            EmitPreExitLines();
            HasExited = true;
        }

        return Task.FromResult(ExitsWithinTimeout);
    }

    // The unbounded wait (Call-mode transcription): models a batch job that always runs to
    // completion, emitting its final lines and then exiting. Cancellation is the only early
    // exit, surfaced as a cancelled task so the stop sequence's kill-on-cancel path runs.
    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        EmitPreExitLines();
        HasExited = true;
        return Task.CompletedTask;
    }

    private void EmitPreExitLines()
    {
        foreach (string line in OutputLinesToEmitBeforeExit)
        {
            OutputLineReceived?.Invoke(line);
        }

        foreach (string line in ErrorLinesToEmitBeforeExit)
        {
            ErrorLineReceived?.Invoke(line);
        }
    }

    public void Kill()
    {
        WasKilled = true;
        HasExited = true;
    }

    internal void EmitOutputLine(string line) => OutputLineReceived?.Invoke(line);

    internal void EmitErrorLine(string line) => ErrorLineReceived?.Invoke(line);
}
