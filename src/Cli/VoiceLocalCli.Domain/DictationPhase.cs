namespace VoiceLocalCli.Domain;

/// <summary>
/// The UI-facing phase of a running Dictate session, derived from the engine's
/// <c>@@STATE:</c> events (see src/engine/CONTRACT.md). Mirrors dictate.py's event names
/// one-to-one except for <see cref="Unknown"/>, which has no engine-side counterpart and
/// only exists before the first event arrives.
/// </summary>
public enum DictationPhase
{
    /// <summary>No status event has arrived yet (engine still starting up).</summary>
    Unknown,

    /// <summary>Engine state "listening" -- waiting for speech.</summary>
    Listening,

    /// <summary>Engine state "transcribing" -- a speech chunk is being transcribed.</summary>
    Transcribing,

    /// <summary>Engine state "typing" -- transcribed text is being typed into the focused window.</summary>
    Typing,

    /// <summary>Engine state "focus_mismatch" -- focus changed mid-chunk; the text was logged, not typed.</summary>
    FocusMismatch,

    /// <summary>Engine state "finished" -- the engine process is about to exit normally.</summary>
    Finished,
}
