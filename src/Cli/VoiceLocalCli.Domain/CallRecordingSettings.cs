namespace VoiceLocalCli.Domain;

/// <summary>
/// The tunable parameters transcribe.py accepts, mirrored here the same way
/// <see cref="DictationSettings"/> mirrors dictate.py's -- see src/engine/CONTRACT.md.
/// Speaker diarization is not a flag here: transcribe.py enables it automatically when an
/// <c>HF_TOKEN</c> environment variable is present and skips it with a warning otherwise
/// (see <c>CallRecordingSession</c>), rather than this settings record deciding it.
/// </summary>
/// <param name="Microphone">Device name passed through to ffmpeg's dshow input.</param>
/// <param name="Backend">"auto", "cpu", or "gpu".</param>
/// <param name="Language">Whisper language code, e.g. "de".</param>
public sealed record CallRecordingSettings(
    string Microphone,
    string Backend = "auto",
    string Language = "de");
