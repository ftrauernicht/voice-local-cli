using System.Text.Json;

using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.UseCases;

/// <summary>
/// Classifies one raw line from the engine's stdout per the contract in
/// src/engine/CONTRACT.md: a line starting with the literal <c>@@STATE:</c> prefix is a
/// status event (JSON after the prefix); anything else is transcript content. Pure
/// function -- the entire reason this exists is to keep "is this line a status event"
/// testable without a live process, and to isolate the one place that needs to change if
/// the contract's prefix or JSON shape ever changes.
/// </summary>
public static class EngineLineClassifier
{
    public const string StatePrefix = "@@STATE:";

    public static EngineEvent Classify(string rawLine)
    {
        ArgumentNullException.ThrowIfNull(rawLine);

        if (!rawLine.StartsWith(StatePrefix, StringComparison.Ordinal))
        {
            return new EngineTranscriptLine(rawLine);
        }

        string jsonPayload = rawLine[StatePrefix.Length..];
        try
        {
            using JsonDocument document = JsonDocument.Parse(jsonPayload);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("name", out JsonElement nameElement) || nameElement.ValueKind != JsonValueKind.String)
            {
                return new UnrecognizedEngineLine(rawLine);
            }

            string name = nameElement.GetString()!;
            var fields = new Dictionary<string, string>();
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (property.NameEquals("name"))
                {
                    continue;
                }

                fields[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString()!,
                    _ => property.Value.GetRawText(),
                };
            }

            return new EngineStateEvent(name, fields);
        }
        catch (JsonException)
        {
            return new UnrecognizedEngineLine(rawLine);
        }
    }

    /// <summary>Maps an <see cref="EngineStateEvent"/>'s name to the UI-facing
    /// <see cref="DictationPhase"/>, per the vocabulary table in CONTRACT.md. Unknown
    /// names map to <see cref="DictationPhase.Unknown"/> rather than throwing -- a new
    /// engine-side event the UI doesn't yet render is not a fatal condition.</summary>
    public static DictationPhase ToPhase(EngineStateEvent stateEvent) => stateEvent.Name switch
    {
        "listening" => DictationPhase.Listening,
        "transcribing" => DictationPhase.Transcribing,
        "typing" => DictationPhase.Typing,
        "focus_mismatch" => DictationPhase.FocusMismatch,
        "finished" => DictationPhase.Finished,
        _ => DictationPhase.Unknown,
    };
}
