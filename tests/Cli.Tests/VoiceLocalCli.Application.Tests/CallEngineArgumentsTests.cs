using VoiceLocalCli.Application.UseCases;
using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.Tests;

public sealed class CallEngineArgumentsTests
{
    [Fact]
    public void BuildsTheExpectedArguments()
    {
        var settings = new CallRecordingSettings(Microphone: "Mic", Backend: "cpu", Language: "en");

        IReadOnlyList<string> args = CallEngineArguments.For(@"C:\engine\transcribe.py", @"C:\recordings\call_1.wav", settings);

        Assert.Equal(
            [@"C:\engine\transcribe.py", @"C:\recordings\call_1.wav", "--backend", "cpu", "--language", "en"],
            args);
    }

    [Fact]
    public void NeverIncludesADiarizationFlag()
    {
        // transcribe.py decides diarization from the HF_TOKEN environment variable, not a
        // CLI flag -- see CallRecordingSession, which is where that variable gets set.
        IReadOnlyList<string> args = CallEngineArguments.For(@"C:\engine\transcribe.py", @"C:\recordings\call_1.wav", new CallRecordingSettings(Microphone: "Mic"));

        Assert.DoesNotContain(args, a => a.Contains("diariz", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("", "audio.wav")]
    [InlineData("script.py", "")]
    public void RejectsBlankArguments(string scriptPath, string audioPath)
    {
        Assert.Throws<ArgumentException>(() =>
            CallEngineArguments.For(scriptPath, audioPath, new CallRecordingSettings(Microphone: "Mic")));
    }
}
