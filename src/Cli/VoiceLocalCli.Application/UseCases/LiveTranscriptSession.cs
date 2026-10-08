using VoiceLocalCli.Application.Ports;
using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.UseCases;

/// <summary>
/// Orchestrates one Live-mode run: launches ffmpeg (continuous capture, see
/// <see cref="FfmpegArguments.ForContinuousCapture"/>) and transcribe_live.py as two direct
/// children, relays recognized transcript lines and the engine's raw status-event names
/// (see src/engine/CONTRACT.md) for the UI to render, and stops both the same way
/// <see cref="DictationSession"/> does. Deliberately not sharing a base class with it --
/// the two differ enough (segmented vs. continuous capture, typed
/// <see cref="DictationPhase"/> vs. the raw event name here) that a shared base would
/// mostly be a thin wrapper fighting its own abstraction; see
/// <see cref="ChildProcessStopSequence"/> for the one piece that genuinely is shared.
/// </summary>
public sealed class LiveTranscriptSession(
    IProcessLauncher launcher,
    string ffmpegPath,
    string pythonPath,
    string engineScriptPath,
    string recordingsDirectory)
{
    private static readonly TimeSpan FfmpegStopTimeout = TimeSpan.FromSeconds(3);

    // transcribe_live.py only notices the recording has stopped once the file goes stale
    // (--stale-after, 20s default) -- the engine's own stop timeout has to comfortably
    // clear that plus time for a final chunk's transcription, not just the fixed 3s ffmpeg
    // gets.
    private static readonly TimeSpan EngineStopTimeout = TimeSpan.FromSeconds(45);

    private readonly IProcessLauncher _launcher = launcher;
    private readonly string _ffmpegPath = ffmpegPath;
    private readonly string _pythonPath = pythonPath;
    private readonly string _engineScriptPath = engineScriptPath;
    private readonly string _recordingsDirectory = recordingsDirectory;

    private IChildProcess? _ffmpeg;
    private IChildProcess? _engine;

    /// <summary>The engine's last-seen <c>@@STATE:</c> event name (e.g. "recording",
    /// "transcribing_chunk", "finished"), or "starting" before the first one arrives.
    /// Not a typed enum like <see cref="DictationPhase"/> -- CONTRACT.md's event-name
    /// vocabulary already is the contract, and this session has no typing-focus-mismatch
    /// handling that would benefit from a richer type.</summary>
    public string Phase { get; private set; } = "starting";

    public bool IsRunning => _ffmpeg is not null && !_ffmpeg.HasExited;

    public event Action<string>? PhaseChanged;

    public event Action<TranscriptEntry>? TranscriptReceived;

    public event Action<string>? UnrecognizedEngineOutput;

    /// <summary>Starts capture and live transcription. Returns the transcript file path
    /// the engine writes to.</summary>
    public string Start(LiveTranscriptSettings settings, Func<DateTimeOffset>? clock = null)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("A live transcript session is already running.");
        }

        DateTimeOffset now = (clock ?? (() => DateTimeOffset.Now))();
        string stamp = now.ToString("yyyy-MM-dd_HH-mm-ss");
        string audioPath = Path.Combine(_recordingsDirectory, $"live_{stamp}.wav");
        string outPath = Path.Combine(_recordingsDirectory, $"live_{stamp}.live.txt");

        IReadOnlyList<string> ffmpegArgs = FfmpegArguments.ForContinuousCapture(settings.Microphone, audioPath);
        _ffmpeg = _launcher.Start(_ffmpegPath, ffmpegArgs);

        IReadOnlyList<string> engineArgs = LiveEngineArguments.For(_engineScriptPath, audioPath, settings, outPath);
        _engine = _launcher.Start(_pythonPath, engineArgs);
        _engine.OutputLineReceived += HandleEngineOutputLine;

        return outPath;
    }

    /// <summary>Stops the session: the same ffmpeg stdin-'q' mechanism
    /// <see cref="DictationSession.StopAsync"/> uses (see its doc comment for how that was
    /// confirmed against the real ffmpeg build), then waits for the engine to notice the
    /// recording went stale and exit on its own.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_ffmpeg is null)
        {
            return;
        }

        await ChildProcessStopSequence.SignalThenKillIfNeededAsync(_ffmpeg, "q", FfmpegStopTimeout, cancellationToken).ConfigureAwait(false);

        if (_engine is not null)
        {
            await ChildProcessStopSequence.WaitThenKillIfNeededAsync(_engine, EngineStopTimeout, cancellationToken).ConfigureAwait(false);
        }
    }

    private void HandleEngineOutputLine(string rawLine)
    {
        EngineEvent evt = EngineLineClassifier.Classify(rawLine);
        switch (evt)
        {
            case EngineStateEvent stateEvent:
                Phase = stateEvent.Name;
                PhaseChanged?.Invoke(Phase);
                break;
            case EngineTranscriptLine transcriptLine:
                TranscriptReceived?.Invoke(new TranscriptEntry(DateTimeOffset.Now, transcriptLine.Text));
                break;
            case UnrecognizedEngineLine unrecognized:
                UnrecognizedEngineOutput?.Invoke(unrecognized.RawLine);
                break;
        }
    }
}
