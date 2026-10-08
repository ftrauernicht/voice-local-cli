namespace VoiceLocalCli.Application.UseCases;

/// <summary>
/// Builds the ffmpeg argument list for a Dictate-mode capture: one microphone device,
/// recorded through the <c>segment</c> muxer into rotating files (mirrors
/// Record-Dictate.ps1's historical invocation, now retired -- see the repo README's
/// "Roadmap"). Pure function: no process is started here, so this is fully unit-testable.
/// </summary>
public static class FfmpegArguments
{
    public static IReadOnlyList<string> ForDictateCapture(string microphoneDevice, string segmentPrefix, int segmentSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(microphoneDevice);
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentPrefix);
        return segmentSeconds <= 0
            ? throw new ArgumentOutOfRangeException(nameof(segmentSeconds), segmentSeconds, "Must be positive.")
            : [
            "-f", "dshow",
            "-i", $"audio={microphoneDevice}",
            "-ar", "16000",
            "-ac", "1",
            "-f", "segment",
            "-segment_time", segmentSeconds.ToString(),
            "-reset_timestamps", "1",
            $"{segmentPrefix}_%03d.wav",
        ];
    }
}
