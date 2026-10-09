using System.Diagnostics;

using Spectre.Console;

using Velopack;

using VoiceLocalCli.Application.Ports;
using VoiceLocalCli.Application.UseCases;
using VoiceLocalCli.Domain;
using VoiceLocalCli.Infrastructure;
using VoiceLocalCli.Ui.Console;

// Must run before anything else: when launched by a Velopack-produced installer/updater
// with special hook arguments (first-run, after-update, uninstall, ...), this handles
// them and exits immediately -- it must never run any later code in that case. A no-op
// for a normal launch (dotnet run, or a real install's normal start).
VelopackApp.Build().Run();

AnsiConsole.Write(new FigletText("voice-local-cli").Color(new Color(0x58, 0xA6, 0xFF)));
AnsiConsole.MarkupLine("[grey]Local, offline voice dictation -- speak, and the text types into whatever window has focus.[/]\n");

var updateChecker = new VelopackUpdateChecker();
AvailableUpdate? availableUpdate = await CheckForUpdateWithTimeoutAsync(updateChecker).ConfigureAwait(false);
if (availableUpdate is not null)
{
    AnsiConsole.MarkupLine($"[#58A6FF]Update available:[/] v{availableUpdate.Version.EscapeMarkup()} -- see Settings to update.\n");
}

ISettingsStore settingsStore = new JsonSettingsStore();

string repositoryRoot;
try
{
    repositoryRoot = RepositoryLayout.FindRepositoryRoot();
}
catch (InvalidOperationException ex)
{
    AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]");
    return 1;
}

string pythonExecutable = RepositoryLayout.EnginePythonExecutable(repositoryRoot);
string? ffmpegExecutable = ResolveFfmpegPath();

IReadOnlyList<EnvironmentCheckItem> environmentChecks = EnvironmentCheck.Collect(repositoryRoot, pythonExecutable, ffmpegExecutable);
EnvironmentCheck.Print(environmentChecks);

if (environmentChecks.Any(item => item.Status == EnvironmentCheckStatus.Error) || ffmpegExecutable is null)
{
    // The second condition can never trigger on its own -- a null ffmpegExecutable always
    // produces an Error item above -- it exists only to narrow the type for the compiler's
    // nullable analysis below, which can't follow that invariant through the lambda.
    return 1;
}

OrchestratorMode? modeArgument = ParseModeArgument(args);
if (modeArgument is not null)
{
    // --mode bypass (desktop shortcuts): run once and exit -- no menu involved, so there's
    // nothing to loop back to.
    return await RunSelectedModeAsync(modeArgument.Value, repositoryRoot, pythonExecutable, ffmpegExecutable, settingsStore).ConfigureAwait(false);
}

const string settingsChoice = "Settings";
const string exitChoice = "Exit";

while (true)
{
    string selection = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("What do you want to do?")
            .AddChoiceGroup("Modes", ["Dictate", "Live", "Call"])
            .AddChoices(settingsChoice, exitChoice));

    if (selection == exitChoice)
    {
        return 0;
    }

    if (selection == settingsChoice)
    {
        await RunSettingsMenuAsync(updateChecker, availableUpdate, settingsStore).ConfigureAwait(false);
        continue;
    }

    if (!Enum.TryParse(selection, out OrchestratorMode selectedMode))
    {
        // The "Modes" group header itself isn't a real choice -- selecting it (if Spectre
        // even allows that) just redraws the menu instead of crashing on a bad Enum.Parse.
        continue;
    }

    await RunSelectedModeAsync(selectedMode, repositoryRoot, pythonExecutable, ffmpegExecutable, settingsStore).ConfigureAwait(false);
}

// Dictate uses the plain "mic" device, Live/Call the mixed "call" bus -- see
// Set-AudioDevices.ps1's own doc comment for the distinction. Resolved here, not inside
// each RunXAsync, since all three need it the same way: use what's already configured,
// or configure it now via the real script (see ResolveDevice's own doc comment).
static async Task<int> RunSelectedModeAsync(OrchestratorMode mode, string repositoryRoot, string pythonExecutable, string ffmpegExecutable, ISettingsStore settingsStore)
{
    string devicePurpose = mode == OrchestratorMode.Dictate ? "mic" : "call";
    string device = ResolveDevice(repositoryRoot, devicePurpose);
    OrchestratorSettings orchestratorSettings = await settingsStore.LoadAsync().ConfigureAwait(false);

    return mode switch
    {
        OrchestratorMode.Dictate => await RunDictateAsync(repositoryRoot, pythonExecutable, ffmpegExecutable, device, orchestratorSettings.RecordingsDirectory).ConfigureAwait(false),
        OrchestratorMode.Live => await RunLiveAsync(repositoryRoot, pythonExecutable, ffmpegExecutable, device, orchestratorSettings.RecordingsDirectory).ConfigureAwait(false),
        OrchestratorMode.Call => await RunCallAsync(repositoryRoot, pythonExecutable, ffmpegExecutable, device, orchestratorSettings.RecordingsDirectory).ConfigureAwait(false),
        _ => 1,
    };
}

static async Task<int> RunDictateAsync(string repositoryRoot, string pythonExecutable, string ffmpegExecutable, string microphone, string? recordingsDirectoryOverride)
{
    var settings = new DictationSettings(Microphone: microphone);
    var session = new DictationSession(
        new RealProcessLauncher(),
        ffmpegExecutable,
        pythonExecutable,
        RepositoryLayout.EngineScript(repositoryRoot, "dictate.py"),
        RepositoryLayout.RecordingsDirectory(repositoryRoot, recordingsDirectoryOverride));

    var view = new DictationView();
    session.PhaseChanged += view.OnPhaseChanged;
    session.TranscriptReceived += view.OnTranscriptReceived;
    session.UnrecognizedEngineOutput += line =>
        AnsiConsole.MarkupLine($"[yellow]Unrecognized engine output, ignored by the UI:[/] {line.EscapeMarkup()}");

    bool stopRequested = false;
    bool sessionEnded = false;
    using var cancellation = new CancellationTokenSource();

    ConsoleCtrlHandler.Register(() =>
    {
        stopRequested = true;
        session.StopAsync().GetAwaiter().GetResult();
    });

    Task keyWatcher = WatchForEscapeAsync(() => stopRequested, () => sessionEnded, v => stopRequested = v);

    string logPath = session.Start(settings);
    AnsiConsole.MarkupLine($"[grey]Transcript log: {logPath.EscapeMarkup()}[/]\n");

    await view.RunAsync(() => stopRequested, () => sessionEnded, cancellation.Token);

    if (stopRequested)
    {
        await session.StopAsync();
    }

    await keyWatcher;
    AnsiConsole.MarkupLine("\n[green]Dictation finished.[/]");
    return 0;
}

static async Task<int> RunLiveAsync(string repositoryRoot, string pythonExecutable, string ffmpegExecutable, string microphone, string? recordingsDirectoryOverride)
{
    PrintCallConsentWarning();

    var settings = new LiveTranscriptSettings(Microphone: microphone);
    var session = new LiveTranscriptSession(
        new RealProcessLauncher(),
        ffmpegExecutable,
        pythonExecutable,
        RepositoryLayout.EngineScript(repositoryRoot, "transcribe_live.py"),
        RepositoryLayout.RecordingsDirectory(repositoryRoot, recordingsDirectoryOverride));

    var view = new LiveTranscriptView();
    session.PhaseChanged += view.OnPhaseChanged;
    session.TranscriptReceived += view.OnTranscriptReceived;
    session.UnrecognizedEngineOutput += line =>
        AnsiConsole.MarkupLine($"[yellow]Unrecognized engine output, ignored by the UI:[/] {line.EscapeMarkup()}");

    bool stopRequested = false;
    bool sessionEnded = false;
    using var cancellation = new CancellationTokenSource();

    ConsoleCtrlHandler.Register(() =>
    {
        stopRequested = true;
        session.StopAsync().GetAwaiter().GetResult();
    });

    Task keyWatcher = WatchForEscapeAsync(() => stopRequested, () => sessionEnded, v => stopRequested = v);

    string outPath = session.Start(settings);
    AnsiConsole.MarkupLine($"[grey]Live transcript: {outPath.EscapeMarkup()}[/]\n");

    await view.RunAsync(() => stopRequested, () => sessionEnded, cancellation.Token);

    if (stopRequested)
    {
        await session.StopAsync();
    }

    await keyWatcher;
    AnsiConsole.MarkupLine("\n[green]Live transcript finished.[/]");
    return 0;
}

static async Task<int> RunCallAsync(string repositoryRoot, string pythonExecutable, string ffmpegExecutable, string microphone, string? recordingsDirectoryOverride)
{
    PrintCallConsentWarning();

    var settings = new CallRecordingSettings(Microphone: microphone);
    var session = new CallRecordingSession(
        new RealProcessLauncher(),
        ffmpegExecutable,
        pythonExecutable,
        RepositoryLayout.EngineScript(repositoryRoot, "transcribe.py"),
        RepositoryLayout.RecordingsDirectory(repositoryRoot, recordingsDirectoryOverride));

    session.UnrecognizedEngineOutput += line =>
        AnsiConsole.MarkupLine($"[yellow]Unrecognized engine output, ignored by the UI:[/] {line.EscapeMarkup()}");

    bool stopRequested = false;
    bool windowClosing = false;
    using var cancellation = new CancellationTokenSource();

    ConsoleCtrlHandler.Register(() =>
    {
        // Only the quick, bounded recording-stop path here -- a full transcription pass
        // can take minutes, far longer than Windows gives a CTRL_CLOSE_EVENT handler
        // before killing the process regardless. The raw recording stays on disk either
        // way (see StopRecordingOnlyAsync's own doc comment).
        windowClosing = true;
        stopRequested = true;
        session.StopRecordingOnlyAsync().GetAwaiter().GetResult();
    });

    Task keyWatcher = WatchForEscapeAsync(() => stopRequested, () => false, v => stopRequested = v);

    DateTimeOffset startedAt = DateTimeOffset.Now;
    string audioPath = session.StartRecording(settings);
    AnsiConsole.MarkupLine($"[grey]Recording to: {audioPath.EscapeMarkup()}[/]\n");

    await AnsiConsole.Live(BuildRecordingLayout(startedAt))
        .StartAsync(async ctx =>
        {
            while (!stopRequested && !cancellation.IsCancellationRequested)
            {
                ctx.UpdateTarget(BuildRecordingLayout(startedAt));
                ctx.Refresh();
                await Task.Delay(TimeSpan.FromMilliseconds(150), cancellation.Token).ConfigureAwait(false);
            }

            ctx.UpdateTarget(BuildRecordingLayout(startedAt));
            ctx.Refresh();
        })
        .ConfigureAwait(false);

    await keyWatcher;

    if (windowClosing)
    {
        return 0;
    }

    string? hfToken = await ResolveHfTokenAsync(repositoryRoot).ConfigureAwait(false);
    if (hfToken is null)
    {
        AnsiConsole.MarkupLine("[grey]No Hugging Face token found -- transcribing without speaker diarization. See README.md.[/]");
    }

    string? transcriptOutputPath = null;
    await AnsiConsole.Status().StartAsync("Transcribing...", async ctx =>
    {
        session.PhaseChanged += phase => ctx.Status(phase switch
        {
            "transcribing_full" => "Transcribing the recording...",
            "diarizing_speakers" => "Identifying speakers...",
            "finished" => "Finishing up...",
            _ => phase,
        });
        transcriptOutputPath = await session.StopAndTranscribeAsync(hfToken, cancellation.Token).ConfigureAwait(false);
    }).ConfigureAwait(false);

    if (transcriptOutputPath is not null)
    {
        AnsiConsole.MarkupLine($"\n[green]Done:[/] {transcriptOutputPath.EscapeMarkup()}");
    }
    else
    {
        AnsiConsole.MarkupLine("\n[yellow]Transcription did not finish in time or failed -- the recording is still on disk; check it directly.[/]");
    }

    return 0;
}

// Shown right before Live/Call actually starts recording -- both modes capture a mixed
// call-audio bus (the remote party included, e.g. everyone in a Microsoft Teams meeting),
// unlike Dictate, which only ever records the operator's own microphone. See the matching
// callout in README.md; this is the same warning surfaced where it can't be missed, not a
// duplicate decision -- the actual legal/consent judgment call stays with the operator.
static void PrintCallConsentWarning()
{
    var panel = new Panel(new Markup(
        "This mode records and transcribes the [bold]full audio of a call[/] -- everyone on it, " +
        "not just you (e.g. everyone in a Microsoft Teams meeting).\n" +
        "In Germany, recording or transcribing another person's spoken words without their " +
        "knowledge is a criminal offense under [bold]§ 201 StGB[/], and similar consent " +
        "requirements exist in most other jurisdictions.\n\n" +
        "[bold]Only use this with the explicit, informed consent of everyone on the call.[/]"))
        .Header("[red bold]Warning -- this records other people[/]")
        .BorderColor(Color.Red)
        .Expand();

    AnsiConsole.Write(panel);
    AnsiConsole.WriteLine();
}

static Layout BuildRecordingLayout(DateTimeOffset startedAt)
{
    TimeSpan elapsed = DateTimeOffset.Now - startedAt;
    var header = new Panel(new Markup($"\U0001F442 [#58A6FF]Recording[/]  [grey]{elapsed:hh\\:mm\\:ss}[/]"))
        .Header("voice-local-cli -- Call")
        .Expand();
    var footer = new Markup("[grey]Press[/] [bold]Esc[/] [grey]to stop and start transcribing.[/]");
    return LiveLayoutRenderer.Build(header, [], "Recording -- nothing to transcribe until you stop.", footer);
}

// Watches for the Esc keypress on a background thread and calls requestStop when it's
// pressed, exiting once either stopRequested or sessionEnded is already true (set from
// elsewhere, e.g. ConsoleCtrlHandler or the engine finishing on its own) -- shared by all
// three modes' flows. Delegates to ConsoleEscapeWatcher (reads the real console input
// buffer directly) rather than System.Console.KeyAvailable/ReadKey -- see that class's
// own doc comment for why: confirmed live, with the console actually focused, that
// System.Console's own key reading just doesn't see the keypress under this terminal.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
static Task WatchForEscapeAsync(Func<bool> stopRequested, Func<bool> sessionEnded, Action<bool> requestStop) =>
    Task.Run(() => ConsoleEscapeWatcher.Watch(stopRequested, sessionEnded, () => requestStop(true)));

static async Task<string?> ResolveHfTokenAsync(string repositoryRoot)
{
    string scriptPath = Path.Combine(repositoryRoot, "scripts", "Get-HfToken.ps1");
    if (!File.Exists(scriptPath))
    {
        return null;
    }

    var startInfo = new ProcessStartInfo
    {
        FileName = "powershell.exe",
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    startInfo.ArgumentList.Add("-NoProfile");
    startInfo.ArgumentList.Add("-NonInteractive");
    startInfo.ArgumentList.Add("-File");
    startInfo.ArgumentList.Add(scriptPath);
    startInfo.ArgumentList.Add("-Quiet");

    using Process? process = Process.Start(startInfo);
    if (process is null)
    {
        return null;
    }

    string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
    await process.WaitForExitAsync().ConfigureAwait(false);
    string token = output.Trim();
    return token.Length > 0 ? token : null;
}

// Returns the already-configured device for `purpose` ("mic" or "call"), or runs
// Set-AudioDevices.ps1 -- the real, interactive picker script, not a free-text prompt --
// if nothing is configured yet. Falls back to a plain text prompt only if the script
// itself didn't end up producing a value (e.g. it couldn't find ffmpeg, or the user
// closed it without finishing).
static string ResolveDevice(string repositoryRoot, string purpose)
{
    string? configured = RepositoryLayout.ConfiguredDevice(repositoryRoot, purpose);
    if (configured is not null)
    {
        return configured;
    }

    string label = purpose == "mic" ? "microphone" : "call-audio";
    AnsiConsole.MarkupLine($"[grey]No {label} device configured yet -- running Set-AudioDevices.ps1...[/]\n");
    RunSetAudioDevicesInteractively(repositoryRoot);

    configured = RepositoryLayout.ConfiguredDevice(repositoryRoot, purpose);
    return configured ?? AnsiConsole.Ask<string>($"{label} device name (the exact ffmpeg/dshow name):");
}

// Runs Set-AudioDevices.ps1 with no stdio redirection at all, so it inherits this
// process's own console directly -- its Read-Host prompts work exactly as if it had been
// run by hand in this same window, instead of needing a separate PowerShell window the
// way the README used to describe.
static void RunSetAudioDevicesInteractively(string repositoryRoot) =>
    RunPowerShellScriptInteractively(Path.Combine(repositoryRoot, "scripts", "Set-AudioDevices.ps1"));

// Shared with RunSetAudioDevicesInteractively: no stdio redirection, so a script's
// Read-Host (including -AsSecureString, hidden-input prompts like Save-HfToken.ps1's)
// behaves exactly as if it had been run by hand in this same window.
static void RunPowerShellScriptInteractively(string scriptPath)
{
    if (!File.Exists(scriptPath))
    {
        AnsiConsole.MarkupLine($"[red]Could not find {scriptPath.EscapeMarkup()}.[/]");
        return;
    }

    var startInfo = new ProcessStartInfo
    {
        FileName = "powershell.exe",
        UseShellExecute = false,
        WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
    };
    startInfo.ArgumentList.Add("-NoProfile");
    startInfo.ArgumentList.Add("-File");
    startInfo.ArgumentList.Add(scriptPath);

    using Process? process = Process.Start(startInfo);
    process?.WaitForExit();
}

static string? ResolveFfmpegPath() => ResolveOnPath("ffmpeg.exe");

static string? ResolveOnPath(string executableName)
{
    string? pathVariable = Environment.GetEnvironmentVariable("PATH");
    if (pathVariable is null)
    {
        return null;
    }

    foreach (string directory in pathVariable.Split(Path.PathSeparator))
    {
        string candidate = Path.Combine(directory, executableName);
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return null;
}

static OrchestratorMode? ParseModeArgument(string[] commandLineArgs)
{
    for (int i = 0; i < commandLineArgs.Length - 1; i++)
    {
        if (commandLineArgs[i] == "--mode" &&
            Enum.TryParse(commandLineArgs[i + 1], ignoreCase: true, out OrchestratorMode parsed))
        {
            return parsed;
        }
    }

    return null;
}

static async Task RunSettingsMenuAsync(IUpdateChecker updateChecker, AvailableUpdate? availableUpdate, ISettingsStore settingsStore)
{
    const string createShortcutsChoice = "Create desktop shortcuts";
    const string changeDevicesChoice = "Change audio devices";
    const string setFolderChoice = "Set recordings/transcripts folder";
    const string setHfTokenChoice = "Set Hugging Face token";
    const string backChoice = "Back";

    string? updateChoice = availableUpdate is null ? null : $"Update to v{availableUpdate.Version}";
    List<string> choices = [createShortcutsChoice, changeDevicesChoice, setFolderChoice, setHfTokenChoice];
    if (updateChoice is not null)
    {
        choices.Add(updateChoice);
    }

    choices.Add(backChoice);

    string selection = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("Settings")
            .AddChoices(choices));

    if (selection == createShortcutsChoice)
    {
        CreateShortcuts();
    }
    else if (selection == changeDevicesChoice)
    {
        ChangeAudioDevices();
    }
    else if (selection == setFolderChoice)
    {
        await SetRecordingsFolderAsync(settingsStore).ConfigureAwait(false);
    }
    else if (selection == setHfTokenChoice)
    {
        SetHuggingFaceToken();
    }
    else if (selection == updateChoice)
    {
        await AnsiConsole.Status().StartAsync(
            "Downloading update...",
            _ => updateChecker.DownloadAndApplyUpdateAsync()).ConfigureAwait(false);
    }
}

static void ChangeAudioDevices()
{
    string repositoryRoot;
    try
    {
        repositoryRoot = RepositoryLayout.FindRepositoryRoot();
    }
    catch (InvalidOperationException ex)
    {
        AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]");
        return;
    }

    RunSetAudioDevicesInteractively(repositoryRoot);
}

static void SetHuggingFaceToken()
{
    string repositoryRoot;
    try
    {
        repositoryRoot = RepositoryLayout.FindRepositoryRoot();
    }
    catch (InvalidOperationException ex)
    {
        AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]");
        return;
    }

    RunPowerShellScriptInteractively(Path.Combine(repositoryRoot, "scripts", "Save-HfToken.ps1"));
}

static async Task SetRecordingsFolderAsync(ISettingsStore settingsStore)
{
    OrchestratorSettings current = await settingsStore.LoadAsync().ConfigureAwait(false);
    string currentDisplay = string.IsNullOrWhiteSpace(current.RecordingsDirectory)
        ? "(default: <repo>\\recordings)"
        : current.RecordingsDirectory;
    AnsiConsole.MarkupLine($"[grey]Current: {currentDisplay.EscapeMarkup()}[/]");

    string input = AnsiConsole.Ask("New folder (leave blank to reset to the default):", string.Empty);
    string? newValue = string.IsNullOrWhiteSpace(input) ? null : input.Trim();

    await settingsStore.SaveAsync(current with { RecordingsDirectory = newValue }).ConfigureAwait(false);
    AnsiConsole.MarkupLine(newValue is null
        ? "[green]Reset to the default.[/]"
        : $"[green]Saved:[/] {newValue.EscapeMarkup()}");
}

static async Task<AvailableUpdate?> CheckForUpdateWithTimeoutAsync(IUpdateChecker updateChecker)
{
    if (!updateChecker.IsInstalled)
    {
        return null;
    }

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    try
    {
        return await updateChecker.CheckForUpdateAsync(timeout.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        // GitHub unreachable or just slow -- never worth blocking startup over, see
        // IUpdateChecker's own doc comment on why a failed check is never fatal.
        return null;
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
static int CreateShortcuts()
{
    string repositoryRoot;
    try
    {
        repositoryRoot = RepositoryLayout.FindRepositoryRoot();
    }
    catch (InvalidOperationException ex)
    {
        AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]");
        return 1;
    }

    string? dotnetExecutable = ResolveOnPath(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
    if (dotnetExecutable is null)
    {
        AnsiConsole.MarkupLine("[red]Could not find dotnet on PATH.[/]");
        return 1;
    }

    string desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    var useCase = new CreateDesktopShortcutsUseCase(new RealShortcutWriter());
    IReadOnlyList<DesktopShortcutDefinition> created = useCase.Run(repositoryRoot, desktopDirectory, dotnetExecutable);

    foreach (DesktopShortcutDefinition shortcut in created)
    {
        AnsiConsole.MarkupLine($"[green]Created[/] {shortcut.ShortcutPath.EscapeMarkup()}");
    }

    AnsiConsole.MarkupLine(
        "\n[grey]Each shortcut runs 'dotnet run' against this checkout -- if you move or delete this folder, re-run this menu item from the new location instead of expecting the old shortcuts to still work.[/]");
    return 0;
}
