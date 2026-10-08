using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.UseCases;

/// <summary>
/// Builds the argument list for <c>python.exe src/engine/transcribe_live.py ...</c>, from
/// the flags transcribe_live.py's own argparse accepts (see src/engine/CONTRACT.md). Pure
/// function, unit-testable without starting any process.
/// </summary>
public static class LiveEngineArguments
{
    public static IReadOnlyList<string> For(string engineScriptPath, string audioPath, LiveTranscriptSettings settings, string outPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineScriptPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(audioPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outPath);
        ArgumentNullException.ThrowIfNull(settings);

        return
        [
            engineScriptPath,
            audioPath,
            "--interval", settings.IntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--backend", settings.Backend,
            "--language", settings.Language,
            "--stale-after", settings.StaleAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--out", outPath,
        ];
    }
}
