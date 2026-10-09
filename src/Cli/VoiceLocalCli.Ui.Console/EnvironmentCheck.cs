using Spectre.Console;

namespace VoiceLocalCli.Ui.Console;

internal enum EnvironmentCheckStatus
{
    Ok,
    OptionalMissing,
    Error,
}

internal sealed record EnvironmentCheckItem(string Name, EnvironmentCheckStatus Status, string Detail);

/// <summary>
/// A startup summary of everything the orchestrator depends on -- required (the Python
/// engine, ffmpeg) and optional (audio devices, the Hugging Face token) -- printed once,
/// before the user picks a mode. Exists so a missing piece shows up here instead of
/// surfacing mid-session: the Hugging Face token specifically used to only be mentioned
/// right before Call mode's transcription step, after the whole call had already been
/// recorded (see docs/MANUAL_VERIFICATION.md).
/// </summary>
internal static class EnvironmentCheck
{
    /// <param name="scriptsRoot">From <see cref="RepositoryLayout.FindScriptsRoot"/>.</param>
    /// <param name="pythonExecutable">From <see cref="RepositoryLayout.EnginePythonExecutable"/>.</param>
    /// <param name="ffmpegExecutable">Resolved via PATH, or null if not found.</param>
    /// <param name="venvStalenessNote">Non-null when a present venv was bootstrapped
    /// against an older orchestrator version than the one currently running (installed
    /// mode only -- dev-mode venvs are never stamped) -- appended to the Python engine
    /// row's detail rather than treated as an error, since the venv still works.</param>
    internal static IReadOnlyList<EnvironmentCheckItem> Collect(
        string scriptsRoot, string pythonExecutable, string? ffmpegExecutable, string? venvStalenessNote = null)
    {
        return
        [
            File.Exists(pythonExecutable)
                ? new EnvironmentCheckItem("Python engine", EnvironmentCheckStatus.Ok, venvStalenessNote ?? pythonExecutable)
                : new EnvironmentCheckItem("Python engine", EnvironmentCheckStatus.Error, $"Not found at {pythonExecutable} -- run scripts\\Setup.ps1 first."),
            ffmpegExecutable is not null
                ? new EnvironmentCheckItem("ffmpeg", EnvironmentCheckStatus.Ok, ffmpegExecutable)
                : new EnvironmentCheckItem("ffmpeg", EnvironmentCheckStatus.Error, "Not found on PATH -- run scripts\\Setup.ps1 first."),
            RepositoryLayout.ConfiguredDevice(scriptsRoot, "mic") is { } micDevice
                ? new EnvironmentCheckItem("Microphone device", EnvironmentCheckStatus.Ok, micDevice)
                : new EnvironmentCheckItem("Microphone device", EnvironmentCheckStatus.OptionalMissing, "Configured on first use of Dictate."),
            RepositoryLayout.ConfiguredDevice(scriptsRoot, "call") is { } callDevice
                ? new EnvironmentCheckItem("Call-audio device", EnvironmentCheckStatus.Ok, callDevice)
                : new EnvironmentCheckItem("Call-audio device", EnvironmentCheckStatus.OptionalMissing, "Configured on first use of Live/Call."),
            File.Exists(HfTokenFilePath)
                ? new EnvironmentCheckItem("Hugging Face token", EnvironmentCheckStatus.Ok, "Speaker diarization enabled for Call mode.")
                : new EnvironmentCheckItem("Hugging Face token", EnvironmentCheckStatus.OptionalMissing, "Call mode will skip speaker diarization -- Settings > Set Hugging Face token."),
        ];
    }

    internal static void Print(IReadOnlyList<EnvironmentCheckItem> items)
    {
        var table = new Table().Border(TableBorder.None).HideHeaders();
        table.AddColumn(new TableColumn(string.Empty));
        table.AddColumn(new TableColumn(string.Empty));
        table.AddColumn(new TableColumn(string.Empty).PadRight(0));

        foreach (EnvironmentCheckItem item in items)
        {
            string status = item.Status switch
            {
                EnvironmentCheckStatus.Ok => "[#3FB950]OK[/]",
                EnvironmentCheckStatus.OptionalMissing => "[#D29922]not set[/]",
                EnvironmentCheckStatus.Error => "[red]missing[/]",
                _ => string.Empty,
            };

            table.AddRow(
                new Markup(item.Name.EscapeMarkup()),
                new Markup(status),
                new Markup($"[grey]{item.Detail.EscapeMarkup()}[/]"));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static string HfTokenFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "voice-local-cli", "hf-token.dpapi");
}
