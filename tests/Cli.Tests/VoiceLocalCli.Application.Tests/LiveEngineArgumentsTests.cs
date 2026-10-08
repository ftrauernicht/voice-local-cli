using VoiceLocalCli.Application.UseCases;
using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.Tests;

public sealed class LiveEngineArgumentsTests
{
    [Fact]
    public void BuildsTheExpectedArguments()
    {
        var settings = new LiveTranscriptSettings(Microphone: "Mic", IntervalSeconds: 6, Backend: "gpu", Language: "en", StaleAfterSeconds: 15);

        IReadOnlyList<string> args = LiveEngineArguments.For(@"C:\engine\transcribe_live.py", @"C:\recordings\live_1.wav", settings, @"C:\recordings\live_1.live.txt");

        Assert.Equal(
            [@"C:\engine\transcribe_live.py", @"C:\recordings\live_1.wav",
             "--interval", "6", "--backend", "gpu", "--language", "en",
             "--stale-after", "15", "--out", @"C:\recordings\live_1.live.txt"],
            args);
    }

    [Theory]
    [InlineData("", "audio.wav", "out.txt")]
    [InlineData("script.py", "", "out.txt")]
    [InlineData("script.py", "audio.wav", "")]
    public void RejectsBlankArguments(string scriptPath, string audioPath, string outPath)
    {
        Assert.Throws<ArgumentException>(() =>
            LiveEngineArguments.For(scriptPath, audioPath, new LiveTranscriptSettings(Microphone: "Mic"), outPath));
    }
}
