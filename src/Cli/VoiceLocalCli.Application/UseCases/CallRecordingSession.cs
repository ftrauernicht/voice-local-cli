using VoiceLocalCli.Application.Ports;
using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.UseCases;

/// <summary>
/// Orchestrates one Call-mode run: unlike <see cref="DictationSession"/> and
/// <see cref="LiveTranscriptSession"/>, recording and transcription are sequential, not
/// concurrent -- ffmpeg records the whole call first (continuous capture, see
/// <see cref="FfmpegArguments.ForContinuousCapture"/>), and only once that's stopped
/// cleanly (a finalized WAV header, not a hard-killed one -- transcribe.py reads the file
/// with Python's <c>wave</c> module, which needs a correct header) does transcribe.py run
/// once against the finished file, optionally with speaker diarization if an HF token is
/// supplied.
/// </summary>
public sealed class CallRecordingSession(
    IProcessLauncher launcher,
    string ffmpegPath,
    string pythonPath,
    string engineScriptPath,
    string recordingsDirectory)
{
    private static readonly TimeSpan FfmpegStopTimeout = TimeSpan.FromSeconds(3);

    // Full-call transcription plus diarization can legitimately take minutes for a long
    // recording -- generous on purpose, this is not the few-seconds turnaround the other
    // two sessions' engine timeouts assume.
    private static readonly TimeSpan TranscriptionTimeout = TimeSpan.FromMinutes(20);

    private readonly IProcessLauncher _launcher = launcher;
    private readonly string _ffmpegPath = ffmpegPath;
    private readonly string _pythonPath = pythonPath;
    private readonly string _engineScriptPath = engineScriptPath;
    private readonly string _recordingsDirectory = recordingsDirectory;

    private IChildProcess? _ffmpeg;
    private IChildProcess? _transcription;
    private string? _audioPath;
    private CallRecordingSettings? _settings;
    private string? _transcriptOutputPath;

    /// <summary>The last-seen phase: "starting", "recording" (while ffmpeg runs), then
    /// transcribe.py's own event names ("transcribing_full", "diarizing_speakers",
    /// "finished") once <see cref="StopAndTranscribeAsync"/> is called.</summary>
    public string Phase { get; private set; } = "starting";

    public bool IsRecording => _ffmpeg is not null && !_ffmpeg.HasExited;

    public event Action<string>? PhaseChanged;

    public event Action<string>? UnrecognizedEngineOutput;

    /// <summary>Starts recording only -- no transcription yet. Returns the path ffmpeg is
    /// writing to.</summary>
    public string StartRecording(CallRecordingSettings settings, Func<DateTimeOffset>? clock = null)
    {
        if (IsRecording)
        {
            throw new InvalidOperationException("A call recording is already running.");
        }

        DateTimeOffset now = (clock ?? (() => DateTimeOffset.Now))();
        string stamp = now.ToString("yyyy-MM-dd_HH-mm-ss");
        _audioPath = Path.Combine(_recordingsDirectory, $"call_{stamp}.wav");
        _settings = settings;

        IReadOnlyList<string> ffmpegArgs = FfmpegArguments.ForContinuousCapture(settings.Microphone, _audioPath);
        _ffmpeg = _launcher.Start(_ffmpegPath, ffmpegArgs);

        Phase = "recording";
        PhaseChanged?.Invoke(Phase);
        return _audioPath;
    }

    /// <summary>Stops only the recording, without transcribing it -- the bounded,
    /// few-seconds path for when there's no time left for a potentially minutes-long
    /// transcription pass (the console window is closing; Windows gives a
    /// CTRL_CLOSE_EVENT handler only a short grace period before killing the process
    /// regardless). The raw recording stays on disk; <see cref="StopAndTranscribeAsync"/>
    /// is the normal path, run transcribe.py against the file by hand later if this path
    /// was taken instead.</summary>
    public Task StopRecordingOnlyAsync(CancellationToken cancellationToken = default) =>
        _ffmpeg is null
            ? Task.CompletedTask
            : ChildProcessStopSequence.SignalThenKillIfNeededAsync(_ffmpeg, "q", FfmpegStopTimeout, cancellationToken);

    /// <summary>Stops recording, then runs the full transcription (plus speaker diarization
    /// if <paramref name="hfToken"/> is non-empty) against the finished recording. Returns
    /// the path transcribe.py wrote the transcript to -- null if recording was never
    /// started, or if transcription didn't report a "finished" event before
    /// <see cref="TranscriptionTimeout"/> (the process is killed in that case; the
    /// transcript, if any was already written, may still be on disk even though this
    /// returns null).</summary>
    public async Task<string?> StopAndTranscribeAsync(string? hfToken, CancellationToken cancellationToken = default)
    {
        if (_ffmpeg is null || _audioPath is null || _settings is null)
        {
            return null;
        }

        await ChildProcessStopSequence.SignalThenKillIfNeededAsync(_ffmpeg, "q", FfmpegStopTimeout, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<string> engineArgs = CallEngineArguments.For(_engineScriptPath, _audioPath, _settings);
        IReadOnlyDictionary<string, string>? environmentVariables = string.IsNullOrEmpty(hfToken)
            ? null
            : new Dictionary<string, string> { ["HF_TOKEN"] = hfToken };

        _transcription = _launcher.Start(_pythonPath, engineArgs, environmentVariables);
        _transcription.OutputLineReceived += HandleTranscriptionOutputLine;

        await ChildProcessStopSequence.WaitThenKillIfNeededAsync(_transcription, TranscriptionTimeout, cancellationToken).ConfigureAwait(false);
        return _transcriptOutputPath;
    }

    private void HandleTranscriptionOutputLine(string rawLine)
    {
        EngineEvent evt = EngineLineClassifier.Classify(rawLine);
        switch (evt)
        {
            case EngineStateEvent stateEvent:
                Phase = stateEvent.Name;
                PhaseChanged?.Invoke(Phase);
                if (stateEvent.Name == "finished" && stateEvent.Fields.TryGetValue("output", out string? output))
                {
                    _transcriptOutputPath = output;
                }

                break;
            case UnrecognizedEngineLine unrecognized:
                UnrecognizedEngineOutput?.Invoke(unrecognized.RawLine);
                break;
        }
    }
}
