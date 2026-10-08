using VoiceLocalCli.Application.UseCases;
using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.Tests;

public sealed class CallRecordingSessionTests
{
    private static CallRecordingSession CreateSession(FakeProcessLauncher launcher) => new(
        launcher,
        ffmpegPath: @"C:\ffmpeg.exe",
        pythonPath: @"C:\venv\python.exe",
        engineScriptPath: @"C:\engine\transcribe.py",
        recordingsDirectory: @"C:\recordings");

    [Fact]
    public void StartRecordingLaunchesOnlyFfmpegWithAContinuousCaptureFile()
    {
        var launcher = new FakeProcessLauncher();
        var session = CreateSession(launcher);
        var fixedClock = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        string audioPath = session.StartRecording(new CallRecordingSettings(Microphone: "Mic"), () => fixedClock);

        Assert.Single(launcher.StartCalls);
        Assert.Equal(@"C:\ffmpeg.exe", launcher.StartCalls[0].FileName);
        Assert.Equal(@"C:\recordings\call_2026-10-09_12-00-00.wav", audioPath);
        Assert.Equal("recording", session.Phase);
    }

    [Fact]
    public void StartingRecordingTwiceThrows()
    {
        var launcher = new FakeProcessLauncher();
        launcher.EnqueueProcess(new FakeChildProcess { HasExited = false });
        var session = CreateSession(launcher);
        session.StartRecording(new CallRecordingSettings(Microphone: "Mic"));

        Assert.Throws<InvalidOperationException>(() => session.StartRecording(new CallRecordingSettings(Microphone: "Mic")));
    }

    [Fact]
    public async Task StopAndTranscribeWritesQToFfmpegThenLaunchesTranscriptionAfterwards()
    {
        var launcher = new FakeProcessLauncher();
        var ffmpeg = new FakeChildProcess();
        launcher.EnqueueProcess(ffmpeg);
        var transcription = new FakeChildProcess();
        launcher.EnqueueProcess(transcription);
        var session = CreateSession(launcher);
        session.StartRecording(new CallRecordingSettings(Microphone: "Mic"));

        await session.StopAndTranscribeAsync(hfToken: null, TestContext.Current.CancellationToken);

        Assert.Equal(["q"], ffmpeg.StandardInputWrites);
        Assert.Equal(2, launcher.StartCalls.Count);
        Assert.Equal(@"C:\venv\python.exe", launcher.StartCalls[1].FileName);
        Assert.Contains(@"C:\engine\transcribe.py", launcher.StartCalls[1].Arguments);
    }

    [Fact]
    public async Task StopAndTranscribeSetsHfTokenEnvironmentVariableWhenATokenIsSupplied()
    {
        var launcher = new FakeProcessLauncher();
        launcher.EnqueueProcess(new FakeChildProcess());
        launcher.EnqueueProcess(new FakeChildProcess());
        var session = CreateSession(launcher);
        session.StartRecording(new CallRecordingSettings(Microphone: "Mic"));

        await session.StopAndTranscribeAsync(hfToken: "hf_secret", TestContext.Current.CancellationToken);

        IReadOnlyDictionary<string, string>? env = launcher.StartCalls[1].EnvironmentVariables;
        Assert.NotNull(env);
        Assert.Equal("hf_secret", env!["HF_TOKEN"]);
    }

    [Fact]
    public async Task StopAndTranscribeSetsNoEnvironmentVariablesWhenNoTokenIsSupplied()
    {
        var launcher = new FakeProcessLauncher();
        launcher.EnqueueProcess(new FakeChildProcess());
        launcher.EnqueueProcess(new FakeChildProcess());
        var session = CreateSession(launcher);
        session.StartRecording(new CallRecordingSettings(Microphone: "Mic"));

        await session.StopAndTranscribeAsync(hfToken: null, TestContext.Current.CancellationToken);

        Assert.Null(launcher.StartCalls[1].EnvironmentVariables);
    }

    [Fact]
    public async Task StopAndTranscribeReturnsTheOutputPathFromTheFinishedEvent()
    {
        var launcher = new FakeProcessLauncher();
        launcher.EnqueueProcess(new FakeChildProcess());
        var transcription = new FakeChildProcess();
        transcription.OutputLinesToEmitBeforeExit.Add("""@@STATE:{"name": "finished", "output": "C:\\recordings\\call_1.txt"}""");
        launcher.EnqueueProcess(transcription);
        var session = CreateSession(launcher);
        session.StartRecording(new CallRecordingSettings(Microphone: "Mic"));

        string? outputPath = await session.StopAndTranscribeAsync(hfToken: null, TestContext.Current.CancellationToken);

        Assert.Equal(@"C:\recordings\call_1.txt", outputPath);
    }

    [Fact]
    public async Task StopAndTranscribeTracksIntermediatePhasesAsTheyArrive()
    {
        var launcher = new FakeProcessLauncher();
        launcher.EnqueueProcess(new FakeChildProcess());
        var transcription = new FakeChildProcess();
        transcription.OutputLinesToEmitBeforeExit.Add("""@@STATE:{"name": "diarizing_speakers"}""");
        launcher.EnqueueProcess(transcription);
        var session = CreateSession(launcher);
        List<string> observedPhases = [];
        session.PhaseChanged += phase => observedPhases.Add(phase);
        session.StartRecording(new CallRecordingSettings(Microphone: "Mic"));

        await session.StopAndTranscribeAsync(hfToken: null, TestContext.Current.CancellationToken);

        Assert.Contains("recording", observedPhases);
        Assert.Contains("diarizing_speakers", observedPhases);
        Assert.Equal("diarizing_speakers", session.Phase);
    }

    [Fact]
    public async Task StopAndTranscribeKillsTranscriptionIfItDoesNotFinishInTime()
    {
        var launcher = new FakeProcessLauncher();
        launcher.EnqueueProcess(new FakeChildProcess());
        var transcription = new FakeChildProcess { ExitsWithinTimeout = false };
        launcher.EnqueueProcess(transcription);
        var session = CreateSession(launcher);
        session.StartRecording(new CallRecordingSettings(Microphone: "Mic"));

        string? outputPath = await session.StopAndTranscribeAsync(hfToken: null, TestContext.Current.CancellationToken);

        Assert.True(transcription.WasKilled);
        Assert.Null(outputPath);
    }

    [Fact]
    public async Task StopRecordingOnlyWritesQButNeverLaunchesTranscription()
    {
        var launcher = new FakeProcessLauncher();
        var ffmpeg = new FakeChildProcess();
        launcher.EnqueueProcess(ffmpeg);
        var session = CreateSession(launcher);
        session.StartRecording(new CallRecordingSettings(Microphone: "Mic"));

        await session.StopRecordingOnlyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["q"], ffmpeg.StandardInputWrites);
        Assert.Single(launcher.StartCalls); // only ffmpeg -- no transcription process
    }

    [Fact]
    public async Task StopRecordingOnlyBeforeStartingRecordingIsANoOp()
    {
        var launcher = new FakeProcessLauncher();
        var session = CreateSession(launcher);

        await session.StopRecordingOnlyAsync(TestContext.Current.CancellationToken); // must not throw
    }

    [Fact]
    public async Task StopAndTranscribeBeforeStartingRecordingReturnsNull()
    {
        var launcher = new FakeProcessLauncher();
        var session = CreateSession(launcher);

        string? outputPath = await session.StopAndTranscribeAsync(hfToken: null, TestContext.Current.CancellationToken);

        Assert.Null(outputPath);
        Assert.Empty(launcher.StartCalls);
    }
}
