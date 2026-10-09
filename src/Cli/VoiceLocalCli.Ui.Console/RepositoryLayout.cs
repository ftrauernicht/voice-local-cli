using System.Text.Json;

namespace VoiceLocalCli.Ui.Console;

/// <summary>
/// Locates everything this app needs on disk, in one of two shapes depending on how it's
/// running:
/// <list type="bullet">
/// <item>A dev checkout (<c>dotnet run</c>): walks up from the executable looking for
/// <c>VoiceLocalCli.slnx</c>, then resolves the engine/scripts folders relative to that
/// root -- exactly as before this app was ever installed anywhere.</item>
/// <item>An installed copy (no <c>.slnx</c> reachable above the exe): falls back to an
/// <c>engine</c>/<c>scripts</c> folder bundled as a direct sibling of the exe itself (see
/// <c>release.yml</c>, which copies <c>src/engine</c> and <c>scripts</c> into the published
/// package before <c>vpk pack</c>s it).</item>
/// </list>
/// The Python venv is deliberately NOT resolved the same way in both cases -- see
/// <see cref="EngineVenvRoot"/>.
/// </summary>
internal static class RepositoryLayout
{
    private const string SolutionFileName = "VoiceLocalCli.slnx";
    private const string BootstrapVersionFileName = ".bootstrap-version";

    /// <returns>The repository root if this executable is running from inside a real
    /// voice-local-cli checkout, otherwise null -- never throws, unlike the dev-only
    /// <see cref="FindRepositoryRootForShortcuts"/> this used to be.</returns>
    internal static string? TryFindRepositoryRoot()
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

        return null;
    }

    /// <summary>
    /// The repository root, for the one remaining caller that only ever makes sense in dev
    /// mode (desktop shortcuts that run `dotnet run --project ...`) -- throws with a message
    /// naming the real constraint, since there is no bundled fallback that makes sense here.
    /// </summary>
    internal static string FindRepositoryRootForShortcuts() =>
        TryFindRepositoryRoot() ??
        throw new InvalidOperationException(
            $"Could not find '{SolutionFileName}' above {AppContext.BaseDirectory} -- is this executable running from inside the voice-local-cli repository?");

    internal static string FindEngineRoot() => FindBundledOrCheckoutFolder("engine", Path.Combine("src", "engine"));

    internal static string FindScriptsRoot() => FindBundledOrCheckoutFolder("scripts", "scripts");

    private static string FindBundledOrCheckoutFolder(string folderName, string checkoutRelativePath)
    {
        string? repoRoot = TryFindRepositoryRoot();
        if (repoRoot is not null)
        {
            return Path.Combine(repoRoot, checkoutRelativePath);
        }

        string bundled = Path.Combine(AppContext.BaseDirectory, folderName);
        return Directory.Exists(bundled)
            ? bundled
            : throw new InvalidOperationException(
                $"Could not find the '{folderName}' folder -- neither a voice-local-cli checkout above {AppContext.BaseDirectory}, nor a bundled '{folderName}' folder next to it.");
    }

    /// <summary>
    /// Where the Python venv lives. For a dev checkout, inside the engine folder itself,
    /// exactly as `scripts/Setup.ps1` has always done. For an installed copy, a stable,
    /// per-machine location OUTSIDE the Velopack-managed install folder -- Velopack replaces
    /// that folder's entire contents on every update, so a venv living inside it would need
    /// a full multi-hundred-MB `pip install` rebuild on every single app update.
    /// </summary>
    internal static string EngineVenvRoot(string engineRoot, bool isInstalled) =>
        isInstalled
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "voice-local-cli", "engine", ".venv")
            : Path.Combine(engineRoot, ".venv");

    internal static string EnginePythonExecutable(string venvRoot) =>
        Path.Combine(venvRoot, "Scripts", "python.exe");

    internal static string EngineScript(string engineRoot, string scriptFileName) =>
        Path.Combine(engineRoot, scriptFileName);

    /// <param name="baseDefault">Where recordings go when nothing is configured --
    /// precomputed by the caller via <see cref="RecordingsBaseDirectory"/>, since the right
    /// default differs between dev and installed mode.</param>
    /// <param name="overridePath">From <c>OrchestratorSettings.RecordingsDirectory</c> --
    /// null or blank falls back to <paramref name="baseDefault"/>.</param>
    internal static string RecordingsDirectory(string baseDefault, string? overridePath = null)
    {
        string path = string.IsNullOrWhiteSpace(overridePath) ? baseDefault : overridePath;
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// The default recordings location when nothing is configured in Settings: the
    /// checkout's own <c>recordings\</c> folder in dev mode (unchanged), or
    /// <c>%LOCALAPPDATA%\voice-local-cli\recordings</c> for an installed copy, which has no
    /// checkout to put one in.
    /// </summary>
    internal static string RecordingsBaseDirectory(bool isInstalled)
    {
        if (!isInstalled)
        {
            string? repoRoot = TryFindRepositoryRoot();
            if (repoRoot is not null)
            {
                return Path.Combine(repoRoot, "recordings");
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "voice-local-cli", "recordings");
    }

    internal static string DevicesConfigPath(string scriptsRoot) =>
        Path.Combine(scriptsRoot, "devices.local.json");

    /// <param name="scriptsRoot">From <see cref="FindScriptsRoot"/>.</param>
    /// <param name="purpose">"mic" (Dictate) or "call" (Live/Call) -- see
    /// <c>Set-AudioDevices.ps1</c>'s own doc comment for the distinction.</param>
    /// <returns>The configured device name, or null if nothing is configured yet, or the
    /// config file is missing/unreadable.</returns>
    internal static string? ConfiguredDevice(string scriptsRoot, string purpose)
    {
        string path = DevicesConfigPath(scriptsRoot);
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

    /// <returns>The orchestrator version the venv at <paramref name="venvRoot"/> was last
    /// successfully bootstrapped against, or null if it was never stamped (never bootstrapped
    /// by this app, e.g. a dev-mode venv built by hand).</returns>
    internal static string? StampedVenvVersion(string venvRoot)
    {
        string marker = Path.Combine(venvRoot, BootstrapVersionFileName);
        return File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;
    }

    internal static void StampVenvVersion(string venvRoot, string version)
    {
        Directory.CreateDirectory(venvRoot);
        File.WriteAllText(Path.Combine(venvRoot, BootstrapVersionFileName), version);
    }
}
