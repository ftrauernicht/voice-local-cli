namespace VoiceLocalCli.Domain;

/// <summary>
/// The tunable parameters transcribe_live.py accepts, mirrored here the same way
/// <see cref="DictationSettings"/> mirrors dictate.py's -- see src/engine/CONTRACT.md.
/// </summary>
/// <param name="Microphone">Device name passed through to ffmpeg's dshow input.</param>
/// <param name="IntervalSeconds">Seconds between two transcribed chunks.</param>
/// <param name="Backend">"auto", "cpu", or "gpu".</param>
/// <param name="Language">Whisper language code, e.g. "de".</param>
/// <param name="StaleAfterSeconds">Seconds of no file growth before the recording is
/// considered finished.</param>
public sealed record LiveTranscriptSettings(
    string Microphone,
    double IntervalSeconds = 8.0,
    string Backend = "auto",
    string Language = "de",
    double StaleAfterSeconds = 20.0);
