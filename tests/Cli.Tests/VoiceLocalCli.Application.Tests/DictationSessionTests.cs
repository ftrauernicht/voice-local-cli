using VoiceLocalCli.Application.UseCases;
using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.Tests;

public sealed class DictationSessionTests
{
    private static DictationSession CreateSession(FakeProcessLauncher launcher) => new(
        launcher,
        ffmpegPath: @"C:\ffmpeg.exe",
        pythonPath: @"C:\venv\python.exe",
        engineScriptPath: @"C:\engine\dictate.py",
        recordingsDirectory: @"C:\recordings");

    [Fact]
    public void StartLaunchesFfmpegThenTheEngine()
    {
        var launcher = new FakeProcessLauncher();
        var session = CreateSession(launcher);
        var fixedClock = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

        session.Start(new DictationSettings(Microphone: "Mic"), () => fixedClock);

        Assert.Equal(2, launcher.StartCalls.Count);
        Assert.Equal(@"C:\ffmpeg.exe", launcher.StartCalls[0].FileName);
        Assert.Equal(@"C:\venv\python.exe", launcher.StartCalls[1].FileName);
    }

    [Fact]
    public void StartUsesTheClockToNameTheSessionFiles()
    {
        var launcher = new FakeProcessLauncher();
        var session = CreateSession(launcher);
        var fixedClock = new DateTimeOffset(2026, 10, 9, 14, 5, 30, TimeSpan.Zero);

        string logPath = session.Start(new DictationSettings(Microphone: "Mic"), () => fixedClock);

        Assert.Equal(@"C:\recordings\dictate_2026-10-09_14-05-30.dictate.txt", logPath);
        IReadOnlyList<string> ffmpegArgs = launcher.StartCalls[0].Arguments;
        Assert.Contains(ffmpegArgs, a => a.Contains("dictate_2026-10-09_14-05-30"));
    }

    [Fact]
    public void StartingTwiceThrows()
    {
        var launcher = new FakeProcessLauncher();
        launcher.EnqueueProcess(new FakeChildProcess { HasExited = false });
        var session = CreateSession(launcher);
        session.Start(new DictationSettings(Microphone: "Mic"));

        Assert.Throws<InvalidOperationException>(() => session.Start(new DictationSettings(Microphone: "Mic")));
    }

    [Fact]
    public void StateEventFromEngineUpdatesPhaseAndRaisesPhaseChanged()
    {
        var launcher = new FakeProcessLauncher();
        launcher.EnqueueProcess(new FakeChildProcess());
        var engine = new FakeChildProcess();
        launcher.EnqueueProcess(engine);
        var session = CreateSession(launcher);
        DictationPhase? observedPhase = null;
        session.PhaseChanged += phase => observedPhase = phase;

        session.Start(new DictationSettings(Microphone: "Mic"));
        engine.EmitOutputLine("""@@STATE:{"name": "typing", "window": "Notepad"}""");

        Assert.Equal(DictationPhase.Typing, observedPhase);
        Assert.Equal(DictationPhase.Typing, session.Phase);
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

        session.Start(new DictationSettings(Microphone: "Mic"));
        engine.EmitOutputLine("14:32:05 [Notepad] hello world");

        Assert.NotNull(received);
        Assert.Equal("14:32:05 [Notepad] hello world", received!.Text);
    }

    [Fact]
    public void UnparseableStateLineRaisesUnrecognizedEngineOutputNotTranscript()
    {
        var launcher = new FakeProcessLauncher();
        launcher.EnqueueProcess(new FakeChildProcess());
        var engine = new FakeChildProcess();
        launcher.EnqueueProcess(engine);
        var session = CreateSession(launcher);
        string? unrecognized = null;
        TranscriptEntry? wronglyTreatedAsTranscript = null;
        session.UnrecognizedEngineOutput += line => unrecognized = line;
        session.TranscriptReceived += entry => wronglyTreatedAsTranscript = entry;

        session.Start(new DictationSettings(Microphone: "Mic"));
        engine.EmitOutputLine("@@STATE:{broken json");

        Assert.Equal("@@STATE:{broken json", unrecognized);
        Assert.Null(wronglyTreatedAsTranscript);
    }

    [Fact]
    public async Task StopWritesQToFfmpegStandardInput()
    {
        var launcher = new FakeProcessLauncher();
        var ffmpeg = new FakeChildProcess();
        launcher.EnqueueProcess(ffmpeg);
        launcher.EnqueueProcess(new FakeChildProcess { HasExited = true });
        var session = CreateSession(launcher);
        session.Start(new DictationSettings(Microphone: "Mic"));

        await session.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["q"], ffmpeg.StandardInputWrites);
    }

    [Fact]
    public async Task StopKillsFfmpegIfItDoesNotExitWithinTheTimeout()
    {
        var launcher = new FakeProcessLauncher();
        var ffmpeg = new FakeChildProcess { ExitsWithinTimeout = false };
        launcher.EnqueueProcess(ffmpeg);
        launcher.EnqueueProcess(new FakeChildProcess { HasExited = true });
        var session = CreateSession(launcher);
        session.Start(new DictationSettings(Microphone: "Mic"));

        await session.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(ffmpeg.WasKilled);
    }

    [Fact]
    public async Task StopDoesNotKillFfmpegWhenItExitsCleanlyInTime()
    {
        var launcher = new FakeProcessLauncher();
        var ffmpeg = new FakeChildProcess { ExitsWithinTimeout = true };
        launcher.EnqueueProcess(ffmpeg);
        launcher.EnqueueProcess(new FakeChildProcess { HasExited = true });
        var session = CreateSession(launcher);
        session.Start(new DictationSettings(Microphone: "Mic"));

        await session.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(ffmpeg.WasKilled);
    }

    [Fact]
    public async Task StopKillsTheEngineIfItDoesNotSelfTerminateInTime()
    {
        var launcher = new FakeProcessLauncher();
        launcher.EnqueueProcess(new FakeChildProcess { ExitsWithinTimeout = true });
        var engine = new FakeChildProcess { ExitsWithinTimeout = false };
        launcher.EnqueueProcess(engine);
        var session = CreateSession(launcher);
        session.Start(new DictationSettings(Microphone: "Mic"));

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
        session.Start(new DictationSettings(Microphone: "Mic"));
        Assert.True(session.IsRunning);

        ffmpeg.HasExited = true;
        Assert.False(session.IsRunning);
    }
}
