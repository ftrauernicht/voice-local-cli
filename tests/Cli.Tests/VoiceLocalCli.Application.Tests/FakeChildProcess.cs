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
    /// on <see cref="WaitForExitAsync"/> for a process whose exit carries meaning (e.g. its
    /// last <c>@@STATE:</c> event) would have no way to inject that line at the right
    /// moment, since the fake's exit is already-synchronously-completed.</summary>
    public List<string> OutputLinesToEmitBeforeExit { get; } = [];

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
            foreach (string line in OutputLinesToEmitBeforeExit)
            {
                OutputLineReceived?.Invoke(line);
            }

            HasExited = true;
        }

        return Task.FromResult(ExitsWithinTimeout);
    }

    public void Kill()
    {
        WasKilled = true;
        HasExited = true;
    }

    internal void EmitOutputLine(string line) => OutputLineReceived?.Invoke(line);

    internal void EmitErrorLine(string line) => ErrorLineReceived?.Invoke(line);
}
