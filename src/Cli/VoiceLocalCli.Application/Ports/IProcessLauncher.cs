namespace VoiceLocalCli.Application.Ports;

/// <summary>
/// Launches a child process with redirected stdio and no visible window. The one port
/// through which Application code touches the real OS process API -- fake this in tests,
/// never <see cref="System.Diagnostics.Process"/> directly.
/// </summary>
public interface IProcessLauncher
{
    /// <param name="fileName">The executable to launch.</param>
    /// <param name="arguments">Its command-line arguments.</param>
    /// <param name="environmentVariables">Extra variables to set on the child process (for
    /// example HF_TOKEN for transcribe.py's optional speaker diarization) -- merged into,
    /// not replacing, the inherited environment. Omit for a process that needs none.</param>
    IChildProcess Start(string fileName, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environmentVariables = null);
}
