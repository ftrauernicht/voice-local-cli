using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.UseCases;

/// <summary>
/// Builds the argument list for <c>python.exe src/engine/dictate.py ...</c>, from the
/// flags dictate.py's own argparse accepts (see src/engine/CONTRACT.md). Pure function,
/// unit-testable without starting any process.
/// </summary>
public static class DictateEngineArguments
{
    public static IReadOnlyList<string> For(string engineScriptPath, string segmentPrefix, DictationSettings settings, string logPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineScriptPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        ArgumentNullException.ThrowIfNull(settings);

        return
        [
            engineScriptPath,
            segmentPrefix,
            "--pause-seconds", settings.PauseSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--vad-aggressiveness", settings.VadAggressiveness.ToString(),
            "--max-buffer-seconds", settings.MaxBufferSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--stale-after", settings.StaleAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--backend", settings.Backend,
            "--language", settings.Language,
            "--out", logPath,
        ];
    }
}
