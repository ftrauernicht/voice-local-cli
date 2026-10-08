using VoiceLocalCli.Application.Ports;
using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.UseCases;

/// <summary>
/// Orchestrates one Dictate-mode run: launches ffmpeg (capturing the microphone into
/// rotating segment files) and the Python engine (transcribing on real speech pauses,
/// typing into the focused window) as two direct children, classifies the engine's stdout
/// into phase changes and transcript lines, and implements the stop sequence.
///
/// This is the single integration point between Application and the real OS process API
/// (via <see cref="IProcessLauncher"/>) -- everything else here is plain C# logic,
/// unit-testable against a fake launcher. See
/// docs/MANUAL_VERIFICATION.md for what still needs a human running the real executable.
/// </summary>
public sealed class DictationSession(
    IProcessLauncher launcher,
    string ffmpegPath,
    string pythonPath,
    string engineScriptPath,
    string recordingsDirectory)
{
    private static readonly TimeSpan FfmpegStopTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan EngineStopTimeout = TimeSpan.FromSeconds(30);

    private readonly IProcessLauncher _launcher = launcher;
    private readonly string _ffmpegPath = ffmpegPath;
    private readonly string _pythonPath = pythonPath;
    private readonly string _engineScriptPath = engineScriptPath;
    private readonly string _recordingsDirectory = recordingsDirectory;

    private IChildProcess? _ffmpeg;
    private IChildProcess? _engine;

    public DictationPhase Phase { get; private set; } = DictationPhase.Unknown;

    public bool IsRunning => _ffmpeg is not null && !_ffmpeg.HasExited;

    public event Action<DictationPhase>? PhaseChanged;

    public event Action<TranscriptEntry>? TranscriptReceived;

    /// <summary>Raised for an engine line starting with the status-event prefix that
    /// failed to parse -- surfaced rather than silently dropped (see
    /// <see cref="UnrecognizedEngineLine"/>).</summary>
    public event Action<string>? UnrecognizedEngineOutput;

    /// <summary>Starts capture and transcription. Returns the log file path the engine
    /// writes the session's transcript to.</summary>
    public string Start(DictationSettings settings, Func<DateTimeOffset>? clock = null)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("A dictation session is already running.");
        }

        DateTimeOffset now = (clock ?? (() => DateTimeOffset.Now))();
        string stamp = now.ToString("yyyy-MM-dd_HH-mm-ss");
        string segmentPrefix = Path.Combine(_recordingsDirectory, $"dictate_{stamp}");
        string logPath = $"{segmentPrefix}.dictate.txt";

        // 5-minute segments: bounds how much unprocessed audio could be orphaned if the
        // engine process were ever killed mid-segment, mirroring the rationale already
        // documented for the rotating-segment design in src/engine/dictate.py.
        const int segmentSeconds = 300;
        IReadOnlyList<string> ffmpegArgs = FfmpegArguments.ForDictateCapture(settings.Microphone, segmentPrefix, segmentSeconds);
        _ffmpeg = _launcher.Start(_ffmpegPath, ffmpegArgs);

        IReadOnlyList<string> engineArgs = DictateEngineArguments.For(_engineScriptPath, segmentPrefix, settings, logPath);
        _engine = _launcher.Start(_pythonPath, engineArgs);
        _engine.OutputLineReceived += HandleEngineOutputLine;

        return logPath;
    }

    /// <summary>
    /// Stops the session: writes 'q' to ffmpeg's redirected stdin (confirmed, via a
    /// manual spike against the installed ffmpeg build, to stop it as cleanly as typing
    /// 'q' into its own console window would -- see docs/MANUAL_VERIFICATION.md), waits
    /// briefly, and kills the process tree if it doesn't exit in time. The engine process
    /// is expected to notice the last segment file has gone stale and exit on its own
    /// (see dictate.py's own stale-recording detection) -- it gets a longer grace period
    /// before the same kill fallback applies, since it may still be transcribing a final
    /// chunk and running the end-of-session hotword review prompt.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_ffmpeg is null)
        {
            return;
        }

        if (!_ffmpeg.HasExited)
        {
            await _ffmpeg.WriteStandardInputAsync("q", cancellationToken).ConfigureAwait(false);
            bool ffmpegExited = await _ffmpeg.WaitForExitAsync(FfmpegStopTimeout, cancellationToken).ConfigureAwait(false);
            if (!ffmpegExited)
            {
                _ffmpeg.Kill();
            }
        }

        if (_engine is not null && !_engine.HasExited)
        {
            bool engineExited = await _engine.WaitForExitAsync(EngineStopTimeout, cancellationToken).ConfigureAwait(false);
            if (!engineExited)
            {
                _engine.Kill();
            }
        }
    }

    private void HandleEngineOutputLine(string rawLine)
    {
        EngineEvent evt = EngineLineClassifier.Classify(rawLine);
        switch (evt)
        {
            case EngineStateEvent stateEvent:
                Phase = EngineLineClassifier.ToPhase(stateEvent);
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
