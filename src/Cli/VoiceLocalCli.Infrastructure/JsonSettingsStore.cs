using System.Text.Json;

using VoiceLocalCli.Application.Ports;
using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Infrastructure;

/// <summary>
/// The real <see cref="ISettingsStore"/> adapter: a small JSON file under
/// <c>%LOCALAPPDATA%\voice-local-cli\orchestrator-settings.json</c> -- the same
/// per-machine, outside-the-repo location <c>Get-HfToken.ps1</c>/<c>Save-HfToken.ps1</c>
/// already use for the Hugging Face token, so there's one place, not two, for
/// "where does this machine's local config live."
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "voice-local-cli", "orchestrator-settings.json");

    public async Task<OrchestratorSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return new OrchestratorSettings();
        }

        try
        {
            await using FileStream stream = File.OpenRead(FilePath);
            return await JsonSerializer.DeserializeAsync<OrchestratorSettings>(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? new OrchestratorSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Corrupt or unreadable -- treat exactly like "nothing saved yet" rather than
            // crashing the app over a settings file the user can just overwrite via the
            // Settings menu.
            return new OrchestratorSettings();
        }
    }

    public async Task SaveAsync(OrchestratorSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        await using FileStream stream = File.Create(FilePath);
        await JsonSerializer.SerializeAsync(stream, settings, new JsonSerializerOptions { WriteIndented = true }, cancellationToken).ConfigureAwait(false);
    }
}
