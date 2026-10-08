namespace VoiceLocalCli.Domain;

/// <summary>
/// One parsed line of the Python engine's stdout. See src/engine/CONTRACT.md for the
/// wire format this models: a line either starts with the literal <c>@@STATE:</c> prefix
/// (an <see cref="EngineStateEvent"/>) or is plain transcript content
/// (<see cref="EngineTranscriptLine"/>). A line that starts with the prefix but fails to
/// parse as valid JSON is <see cref="UnrecognizedEngineLine"/> -- the contract changed
/// underneath this client, or the engine emitted something unexpected; never silently
/// treated as transcript content.
/// </summary>
public abstract record EngineEvent;

/// <summary>A <c>@@STATE:&lt;json&gt;</c> line, successfully parsed.</summary>
/// <param name="Name">The JSON object's "name" field, e.g. "listening", "typing".</param>
/// <param name="Fields">Every other field in the JSON object, as raw strings (the
/// contract only promises JSON-serializable primitives; callers that need a specific
/// field convert it themselves).</param>
public sealed record EngineStateEvent(string Name, IReadOnlyDictionary<string, string> Fields) : EngineEvent;

/// <summary>A plain stdout line that did not start with the status-event prefix --
/// transcript content, per the contract.</summary>
public sealed record EngineTranscriptLine(string Text) : EngineEvent;

/// <summary>A line starting with the status-event prefix that could not be parsed as the
/// expected JSON shape. Surfaced distinctly rather than silently dropped or treated as
/// transcript text, so a future contract break is visible instead of corrupting the
/// transcript log.</summary>
public sealed record UnrecognizedEngineLine(string RawLine) : EngineEvent;
