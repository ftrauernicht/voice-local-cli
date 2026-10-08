using VoiceLocalCli.Application.UseCases;

namespace VoiceLocalCli.Application.Tests;

public sealed class FfmpegArgumentsTests
{
    [Fact]
    public void BuildsTheExpectedDshowSegmentCapture()
    {
        IReadOnlyList<string> args = FfmpegArguments.ForDictateCapture("My Microphone", @"C:\recordings\dictate_1", 300);

        Assert.Equal(
            ["-f", "dshow", "-i", "audio=My Microphone", "-ar", "16000", "-ac", "1",
             "-f", "segment", "-segment_time", "300", "-reset_timestamps", "1",
             @"C:\recordings\dictate_1_%03d.wav"],
            args);
    }

    [Fact]
    public void DeviceNameWithSpacesStaysAsOneArgumentElement()
    {
        // ArgumentList (not a hand-built string) is what makes this safe -- each element
        // is passed to the process as its own argument, Win32 escaping handled by .NET,
        // not string-concatenated and re-parsed by a shell.
        IReadOnlyList<string> args = FfmpegArguments.ForDictateCapture("Headset Microphone (2- INZONE H9 II)", "prefix", 60);

        Assert.Contains("audio=Headset Microphone (2- INZONE H9 II)", args);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsBlankMicrophoneDevice(string device)
    {
        Assert.Throws<ArgumentException>(() => FfmpegArguments.ForDictateCapture(device, "prefix", 60));
    }

    [Fact]
    public void RejectsNonPositiveSegmentSeconds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegArguments.ForDictateCapture("Mic", "prefix", 0));
    }

    [Fact]
    public void ContinuousCaptureBuildsASingleGrowingFileNotASegmentedOne()
    {
        IReadOnlyList<string> args = FfmpegArguments.ForContinuousCapture("My Microphone", @"C:\recordings\live_1.wav");

        Assert.Equal(
            ["-f", "dshow", "-i", "audio=My Microphone", "-ar", "16000", "-ac", "1", @"C:\recordings\live_1.wav"],
            args);
        Assert.DoesNotContain(args, a => a == "segment");
    }

    [Theory]
    [InlineData("", @"C:\out.wav")]
    [InlineData("Mic", "")]
    public void ContinuousCaptureRejectsBlankArguments(string microphoneDevice, string outputWavPath)
    {
        Assert.Throws<ArgumentException>(() => FfmpegArguments.ForContinuousCapture(microphoneDevice, outputWavPath));
    }
}
