namespace VoiceLocalCli.Application.Ports;

/// <summary>
/// Launches a child process with redirected stdio and no visible window. The one port
/// through which Application code touches the real OS process API -- fake this in tests,
/// never <see cref="System.Diagnostics.Process"/> directly.
/// </summary>
public interface IProcessLauncher
{
    IChildProcess Start(string fileName, IReadOnlyList<string> arguments);
}
