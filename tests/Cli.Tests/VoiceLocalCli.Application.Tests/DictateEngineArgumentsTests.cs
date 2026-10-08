using VoiceLocalCli.Application.UseCases;
using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.Tests;

public sealed class DictateEngineArgumentsTests
{
    [Fact]
    public void BuildsTheExpectedFlagsFromSettings()
    {
        var settings = new DictationSettings(
            Microphone: "Mic",
            PauseSeconds: 0.6,
            VadAggressiveness: 2,
            MaxBufferSeconds: 15.0,
            Backend: "auto",
            Language: "de",
            StaleAfterSeconds: 16.0);

        IReadOnlyList<string> args = DictateEngineArguments.For(@"C:\repo\src\engine\dictate.py", @"C:\recordings\dictate_1", settings, @"C:\recordings\dictate_1.dictate.txt");

        Assert.Equal(
            [
                @"C:\repo\src\engine\dictate.py",
                @"C:\recordings\dictate_1",
                "--pause-seconds", "0.6",
                "--vad-aggressiveness", "2",
                "--max-buffer-seconds", "15",
                "--stale-after", "16",
                "--backend", "auto",
                "--language", "de",
                "--out", @"C:\recordings\dictate_1.dictate.txt",
            ],
            args);
    }

    [Fact]
    public void UsesInvariantCultureForDecimalFlags()
    {
        // Regression guard: a German-locale machine formats 0.6 as "0,6", which argparse
        // on the Python side would reject outright.
        var settings = new DictationSettings(Microphone: "Mic", PauseSeconds: 0.6);

        IReadOnlyList<string> args = DictateEngineArguments.For("dictate.py", "prefix", settings, "log.txt");

        Assert.Contains("0.6", args);
        Assert.DoesNotContain("0,6", args);
    }
}
