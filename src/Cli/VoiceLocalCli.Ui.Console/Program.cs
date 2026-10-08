using Spectre.Console;

using VoiceLocalCli.Application.UseCases;
using VoiceLocalCli.Domain;
using VoiceLocalCli.Infrastructure;
using VoiceLocalCli.Ui.Console;

AnsiConsole.Write(new FigletText("voice-local-cli").Color(new Color(0x58, 0xA6, 0xFF)));
AnsiConsole.MarkupLine("[grey]Local, offline voice dictation -- speak, and the text types into whatever window has focus.[/]\n");

const string createShortcutsChoice = "Create desktop shortcuts";

OrchestratorMode? mode = ParseModeArgument(args);
if (mode is null)
{
    string selection = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("What do you want to do?")
            .AddChoices("Dictate", "Live (coming soon)", "Call (coming soon)", createShortcutsChoice));

    if (selection == createShortcutsChoice)
    {
        return CreateShortcuts();
    }

    mode = selection switch
    {
        "Dictate" => OrchestratorMode.Dictate,
        _ when selection.StartsWith("Live", StringComparison.Ordinal) => OrchestratorMode.Live,
        _ => OrchestratorMode.Call,
    };
}

if (mode != OrchestratorMode.Dictate)
{
    AnsiConsole.MarkupLine("[yellow]This mode isn't wired up yet -- see the repo README's \"Roadmap\".[/]");
    return 0;
}

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
if (!File.Exists(pythonExecutable))
{
    AnsiConsole.MarkupLine($"[red]No Python environment found at {pythonExecutable.EscapeMarkup()}.[/]");
    AnsiConsole.MarkupLine("[grey]Run scripts\\Setup.ps1 first.[/]");
    return 1;
}

string? ffmpegExecutable = ResolveFfmpegPath();
if (ffmpegExecutable is null)
{
    AnsiConsole.MarkupLine("[red]ffmpeg was not found on PATH.[/]");
    AnsiConsole.MarkupLine("[grey]Run scripts\\Setup.ps1 first.[/]");
    return 1;
}

string microphone = AnsiConsole.Ask<string>(
    "Microphone device name (the exact ffmpeg/dshow name, e.g. from Set-AudioDevices.ps1):");

var settings = new DictationSettings(Microphone: microphone);
var session = new DictationSession(
    new RealProcessLauncher(),
    ffmpegExecutable,
    pythonExecutable,
    RepositoryLayout.EngineScript(repositoryRoot),
    RepositoryLayout.RecordingsDirectory(repositoryRoot));

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

var keyWatcher = Task.Run(() =>
{
    while (!stopRequested && !sessionEnded)
    {
        if (System.Console.KeyAvailable && System.Console.ReadKey(intercept: true).Key == ConsoleKey.Escape)
        {
            stopRequested = true;
        }

        Thread.Sleep(50);
    }
});

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
