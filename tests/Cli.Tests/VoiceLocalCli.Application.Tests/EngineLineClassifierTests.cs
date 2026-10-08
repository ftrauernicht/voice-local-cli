using VoiceLocalCli.Application.UseCases;
using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.Tests;

public sealed class EngineLineClassifierTests
{
    // These are real lines captured from a running dictate.py (see src/engine/CONTRACT.md
    // for the format contract), not hand-guessed -- the whole point of this classifier is
    // to stay correct against what the engine actually emits.
    [Theory]
    [InlineData("""@@STATE:{"name": "listening"}""", "listening")]
    [InlineData("""@@STATE:{"name": "transcribing"}""", "transcribing")]
    [InlineData("""@@STATE:{"name": "finished"}""", "finished")]
    public void ParsesRealCapturedStateLinesWithNoExtraFields(string rawLine, string expectedName)
    {
        EngineEvent result = EngineLineClassifier.Classify(rawLine);

        var stateEvent = Assert.IsType<EngineStateEvent>(result);
        Assert.Equal(expectedName, stateEvent.Name);
        Assert.Empty(stateEvent.Fields);
    }

    [Fact]
    public void ParsesTypingEventWithWindowField()
    {
        EngineEvent result = EngineLineClassifier.Classify("""@@STATE:{"name": "typing", "window": "TestWindow"}""");

        var stateEvent = Assert.IsType<EngineStateEvent>(result);
        Assert.Equal("typing", stateEvent.Name);
        Assert.Equal("TestWindow", stateEvent.Fields["window"]);
    }

    [Fact]
    public void ParsesFocusMismatchEventWithAllFields()
    {
        EngineEvent result = EngineLineClassifier.Classify(
            """@@STATE:{"name": "focus_mismatch", "expected": "A", "actual": "B", "text": "hallo welt"}""");

        var stateEvent = Assert.IsType<EngineStateEvent>(result);
        Assert.Equal("focus_mismatch", stateEvent.Name);
        Assert.Equal("A", stateEvent.Fields["expected"]);
        Assert.Equal("B", stateEvent.Fields["actual"]);
        Assert.Equal("hallo welt", stateEvent.Fields["text"]);
    }

    [Fact]
    public void ParsesNonAsciiTextCorrectly()
    {
        // _console.state() deliberately does not escape non-ASCII (ensure_ascii=False) --
        // confirm the classifier round-trips German text unmangled.
        EngineEvent result = EngineLineClassifier.Classify(
            """@@STATE:{"name": "typing", "window": "Grüße"}""");

        var stateEvent = Assert.IsType<EngineStateEvent>(result);
        Assert.Equal("Grüße", stateEvent.Fields["window"]);
    }

    [Fact]
    public void PlainLineIsTranscriptContent()
    {
        EngineEvent result = EngineLineClassifier.Classify("14:32:05 [Visual Studio Code] hello world");

        var transcript = Assert.IsType<EngineTranscriptLine>(result);
        Assert.Equal("14:32:05 [Visual Studio Code] hello world", transcript.Text);
    }

    [Fact]
    public void EmptyLineIsTranscriptContentNotAnError()
    {
        Assert.IsType<EngineTranscriptLine>(EngineLineClassifier.Classify(""));
    }

    [Fact]
    public void PrefixWithInvalidJsonIsUnrecognizedNotTranscript()
    {
        EngineEvent result = EngineLineClassifier.Classify("@@STATE:{not valid json");

        Assert.IsType<UnrecognizedEngineLine>(result);
    }

    [Fact]
    public void PrefixWithJsonMissingNameIsUnrecognized()
    {
        EngineEvent result = EngineLineClassifier.Classify("""@@STATE:{"window": "x"}""");

        Assert.IsType<UnrecognizedEngineLine>(result);
    }

    [Fact]
    public void NullInputThrows()
    {
        Assert.Throws<ArgumentNullException>(() => EngineLineClassifier.Classify(null!));
    }

    [Theory]
    [InlineData("listening", DictationPhase.Listening)]
    [InlineData("transcribing", DictationPhase.Transcribing)]
    [InlineData("typing", DictationPhase.Typing)]
    [InlineData("focus_mismatch", DictationPhase.FocusMismatch)]
    [InlineData("finished", DictationPhase.Finished)]
    [InlineData("some_future_event_name", DictationPhase.Unknown)]
    public void MapsEachKnownEventNameToItsPhase(string eventName, DictationPhase expectedPhase)
    {
        var stateEvent = new EngineStateEvent(eventName, new Dictionary<string, string>());

        Assert.Equal(expectedPhase, EngineLineClassifier.ToPhase(stateEvent));
    }
}
