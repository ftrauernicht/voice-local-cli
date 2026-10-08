using VoiceLocalCli.Application.Ports;

namespace VoiceLocalCli.Infrastructure;

/// <summary>The real <see cref="IProcessLauncher"/> adapter: starts an actual child
/// process with redirected stdio and no visible window, tracked by
/// <see cref="ProcessJobObject"/> for the hard-kill orphan-prevention guarantee.</summary>
public sealed class RealProcessLauncher : IProcessLauncher
{
    public IChildProcess Start(string fileName, IReadOnlyList<string> arguments) =>
        new RealChildProcess(fileName, arguments);
}
