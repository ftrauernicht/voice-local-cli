using System.Text.Json;

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

    internal static string EngineScript(string repositoryRoot, string scriptFileName) =>
        Path.Combine(repositoryRoot, "src", "engine", scriptFileName);

    /// <param name="repositoryRoot">The repository root, from <see cref="FindRepositoryRoot"/>.</param>
    /// <param name="overridePath">From <c>OrchestratorSettings.RecordingsDirectory</c> --
    /// null or blank falls back to the repository's own <c>recordings\</c> folder.</param>
    internal static string RecordingsDirectory(string repositoryRoot, string? overridePath = null)
    {
        string path = string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(repositoryRoot, "recordings")
            : overridePath;
        Directory.CreateDirectory(path);
        return path;
    }

    internal static string DevicesConfigPath(string repositoryRoot) =>
        Path.Combine(repositoryRoot, "scripts", "devices.local.json");

    /// <param name="repositoryRoot">The repository root, from <see cref="FindRepositoryRoot"/>.</param>
    /// <param name="purpose">"mic" (Dictate) or "call" (Live/Call) -- see
    /// <c>Set-AudioDevices.ps1</c>'s own doc comment for the distinction.</param>
    /// <returns>The configured device name, or null if nothing is configured yet, or the
    /// config file is missing/unreadable.</returns>
    internal static string? ConfiguredDevice(string repositoryRoot, string purpose)
    {
        string path = DevicesConfigPath(repositoryRoot);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            string property = purpose == "mic" ? "micDevice" : "callDevice";
            return document.RootElement.TryGetProperty(property, out JsonElement element) && element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
