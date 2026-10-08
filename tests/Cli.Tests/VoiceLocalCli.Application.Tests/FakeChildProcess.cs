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
