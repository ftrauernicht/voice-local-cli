namespace VoiceLocalCli.Ui.Console;

/// <summary>
/// Locates the repo root relative to wherever this executable actually runs from (which
/// varies with build configuration and whether it was published), by walking up from the
/// executable's directory until the solution file is found. Avoids hard-coding a relative
/// path depth that would break between `dotnet run` (bin/Debug/net10.0/) and a published
/// single-file exe (wherever the user puts it, provided the repo root is still reachable
/// above it -- see docs/MANUAL_VERIFICATION.md for the one known constraint this implies).
/// </summary>
internal static class RepositoryLayout
{
    private const string SolutionFileName = "VoiceLocalCli.slnx";

    internal static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find '{SolutionFileName}' above {AppContext.BaseDirectory} -- is this executable running from inside the voice-local-cli repository?");
    }

    internal static string EnginePythonExecutable(string repositoryRoot) =>
        Path.Combine(repositoryRoot, "src", "engine", ".venv", "Scripts", "python.exe");

    internal static string EngineScript(string repositoryRoot) =>
        Path.Combine(repositoryRoot, "src", "engine", "dictate.py");

    internal static string RecordingsDirectory(string repositoryRoot)
    {
        string path = Path.Combine(repositoryRoot, "recordings");
        Directory.CreateDirectory(path);
        return path;
    }
}
