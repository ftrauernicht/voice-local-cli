using VoiceLocalCli.Application.UseCases;

namespace VoiceLocalCli.Application.Tests;

public sealed class EngineStatusLineFormatterTests
{
    [Fact]
    public void StripsAnsiColorCodesAroundAnInfoMessage()
    {
        // The exact bytes src/engine/_console.py's info() writes: ESC[96m ... ESC[0m.
        string rawLine = "\u001b[96mLoading GPU pipeline (OpenVINO, large-v3-turbo) ...\u001b[0m";

        string? result = EngineStatusLineFormatter.Format(rawLine);

        Assert.Equal("Loading GPU pipeline (OpenVINO, large-v3-turbo) ...", result);
    }

    [Fact]
    public void StripsTheOkMarkersOwnColorCodeSeparatelyFromTheMessage()
    {
        // ok()'s own format: ESC[92m[ok]ESC[0m <message>, i.e. two separate color runs.
        string rawLine = "\u001b[92m[ok]\u001b[0m GPU pipeline ready.";

        string? result = EngineStatusLineFormatter.Format(rawLine);

        Assert.Equal("[ok] GPU pipeline ready.", result);
    }

    [Fact]
    public void ReturnsNullForABlankLine()
    {
        Assert.Null(EngineStatusLineFormatter.Format(""));
        Assert.Null(EngineStatusLineFormatter.Format("   "));
    }

    [Fact]
    public void PassesPlainTextThroughUnchanged()
    {
        Assert.Equal("plain stderr line", EngineStatusLineFormatter.Format("plain stderr line"));
    }
}
