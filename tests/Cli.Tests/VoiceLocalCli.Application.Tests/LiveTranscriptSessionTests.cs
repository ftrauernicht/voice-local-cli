using VoiceLocalCli.Application.UseCases;
using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.Tests;

public sealed class LiveTranscriptSessionTests
{
    private static LiveTranscriptSession CreateSession(FakeProcessLauncher launcher) => new(
        launcher,
        ffmpegPath: @"C:\ffmpeg.exe",
        pythonPath: @"C:\venv\python.exe",
        engineScriptPath: @"C:\engine\transcribe_live.py",
        recordingsDirectory: @"C:\recordings");

    [Fact]
    public void StartLaunchesFfmpegThenTheEngineWithAContinuousCaptureFile()
    {
        var launcher = new FakeProcessLauncher();
        var session = CreateSession(launcher);
        var fixedClock = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        string outPath = session.Start(new LiveTranscriptSettings(Microphone: "Mic"), () => fixedClock);

        Assert.Equal(2, launcher.StartCalls.Count);
        Assert.Equal(@"C:\ffmpeg.exe", launcher.StartCalls[0].FileName);
        Assert.Equal(@"C:\venv\python.exe", launcher.StartCalls[1].FileName);
        Assert.Equal(@"C:\recordings\live_2026-10-09_12-00-00.live.txt", outPath);
        Assert.DoesNotContain(launcher.StartCalls[0].Arguments, a => a == "segment");
    }

    [Fact]
    public void StartingTwiceThrows()
    {
        var launcher = new FakeProcessLauncher();
        launcher.EnqueueProcess(new FakeChildProcess { HasExited = false });
        var session = CreateSession(launcher);
        session.Start(new LiveTranscriptSettings(Microphone: "Mic"));

        Assert.Throws<InvalidOperationException>(() => session.Start(new LiveTranscriptSettings(Microphone: "Mic")));
    }

    [Fact]
    public void StateEventFromEngineUpdatesPhaseToTheRawEventName()
    {
        var launcher = new FakeProcessLauncher();
        launcher.EnqueueProcess(new FakeChildProcess());
        var engine = new FakeChildProcess();
        launcher.EnqueueProcess(engine);
        var session = CreateSession(launcher);
        string? observedPhase = null;
        session.PhaseChanged += phase => observedPhase = phase;

        session.Start(new LiveTranscriptSettings(Microphone: "Mic"));
        engine.EmitOutputLine("""@@STATE:{"name": "transcribing_chunk"}""");

        Assert.Equal("transcribing_chunk", observedPhase);
        Assert.Equal("transcribing_chunk", session.Phase);
    }

    [Fact]
    public void TranscriptLineFromEngineRaisesTranscriptReceived()
    {
        var launcher = new FakeProcessLauncher();
        launcher.EnqueueProcess(new FakeChildProcess());
        var engine = new FakeChildProcess();
        launcher.EnqueueProcess(engine);
        var session = CreateSession(launcher);
        TranscriptEntry? received = null;
        session.TranscriptReceived += entry => received = entry;

        session.Start(new LiveTranscriptSettings(Microphone: "Mic"));
        engine.EmitOutputLine("14:32:05 hello world");

        Assert.Equal("14:32:05 hello world", received!.Text);
    }

    [Fact]
    public async Task StopWritesQToFfmpegStandardInput()
    {
        var launcher = new FakeProcessLauncher();
        var ffmpeg = new FakeChildProcess();
        launcher.EnqueueProcess(ffmpeg);
        launcher.EnqueueProcess(new FakeChildProcess { HasExited = true });
        var session = CreateSession(launcher);
        session.Start(new LiveTranscriptSettings(Microphone: "Mic"));

        await session.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["q"], ffmpeg.StandardInputWrites);
    }

    [Fact]
    public async Task StopKillsTheEngineIfItDoesNotNoticeTheStaleRecordingInTime()
    {
        var launcher = new FakeProcessLauncher();
        launcher.EnqueueProcess(new FakeChildProcess { ExitsWithinTimeout = true });
        var engine = new FakeChildProcess { ExitsWithinTimeout = false };
        launcher.EnqueueProcess(engine);
        var session = CreateSession(launcher);
        session.Start(new LiveTranscriptSettings(Microphone: "Mic"));

        await session.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(engine.WasKilled);
    }

    [Fact]
    public async Task StopBeforeStartIsANoOp()
    {
        var launcher = new FakeProcessLauncher();
        var session = CreateSession(launcher);

        await session.StopAsync(TestContext.Current.CancellationToken); // must not throw
    }

    [Fact]
    public void IsRunningReflectsFfmpegExitState()
    {
        var launcher = new FakeProcessLauncher();
        var ffmpeg = new FakeChildProcess { HasExited = false };
        launcher.EnqueueProcess(ffmpeg);
        launcher.EnqueueProcess(new FakeChildProcess());
        var session = CreateSession(launcher);

        Assert.False(session.IsRunning);
        session.Start(new LiveTranscriptSettings(Microphone: "Mic"));
        Assert.True(session.IsRunning);

        ffmpeg.HasExited = true;
        Assert.False(session.IsRunning);
    }
}
