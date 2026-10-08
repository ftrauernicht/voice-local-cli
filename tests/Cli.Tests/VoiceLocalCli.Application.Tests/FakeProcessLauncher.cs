using VoiceLocalCli.Application.Ports;
using VoiceLocalCli.Application.UseCases;

namespace VoiceLocalCli.Application.Tests;

/// <summary>Records every <see cref="Start"/> call and hands back a
/// <see cref="FakeChildProcess"/> per call, in order -- lets a test assert exactly what
/// command lines <see cref="DictationSession"/> launches, and control each process's
/// behavior independently.</summary>
internal sealed class FakeProcessLauncher : IProcessLauncher
{
    private readonly Queue<FakeChildProcess> _processesToReturn = new();

    internal List<(string FileName, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string>? EnvironmentVariables)> StartCalls { get; } = [];

    internal void EnqueueProcess(FakeChildProcess process) => _processesToReturn.Enqueue(process);

    public IChildProcess Start(string fileName, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        StartCalls.Add((fileName, arguments, environmentVariables));
        return _processesToReturn.Count > 0 ? _processesToReturn.Dequeue() : new FakeChildProcess();
    }
}
