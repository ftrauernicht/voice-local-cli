using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.UseCases;

/// <summary>
/// Builds the argument list for <c>python.exe src/engine/transcribe.py ...</c>, from the
/// flags transcribe.py's own argparse accepts (see src/engine/CONTRACT.md). Pure function,
/// unit-testable without starting any process. No diarization flag here: transcribe.py
/// decides on its own, from the HF_TOKEN environment variable
/// (see <see cref="CallRecordingSettings"/>'s own doc comment and
/// <see cref="CallRecordingSession"/>).
/// </summary>
public static class CallEngineArguments
{
    public static IReadOnlyList<string> For(string engineScriptPath, string audioPath, CallRecordingSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineScriptPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(audioPath);
        ArgumentNullException.ThrowIfNull(settings);

        return
        [
            engineScriptPath,
            audioPath,
            "--backend", settings.Backend,
            "--language", settings.Language,
        ];
    }
}
