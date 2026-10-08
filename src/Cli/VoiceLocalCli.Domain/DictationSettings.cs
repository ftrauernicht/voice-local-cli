namespace VoiceLocalCli.Domain;

/// <summary>
/// The tunable parameters dictate.py accepts, mirrored here so the UI layer can collect
/// them without depending on anything outside the domain. Defaults match dictate.py's own
/// argparse defaults (see src/engine/CONTRACT.md) -- kept in sync by hand for now; if this
/// drifts, the engine's own --help output is the source of truth.
/// </summary>
/// <param name="Microphone">Device name passed through to ffmpeg's dshow input.</param>
/// <param name="PauseSeconds">Silence duration that closes off a chunk.</param>
/// <param name="VadAggressiveness">0 (least aggressive at detecting silence) to 3 (most).</param>
/// <param name="MaxBufferSeconds">Safety-net chunk length for a pause-free monologue.</param>
/// <param name="Backend">"auto", "cpu", or "gpu".</param>
/// <param name="Language">Whisper language code, e.g. "de".</param>
/// <param name="StaleAfterSeconds">Seconds of no file growth before the recording is
/// considered finished.</param>
public sealed record DictationSettings(
    string Microphone,
    double PauseSeconds = 0.6,
    int VadAggressiveness = 2,
    double MaxBufferSeconds = 15.0,
    string Backend = "auto",
    string Language = "de",
    double StaleAfterSeconds = 16.0);
