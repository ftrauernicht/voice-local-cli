using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.Ports;

/// <summary>
/// Reads and writes the persisted <see cref="OrchestratorSettings"/>. The one port through
/// which Application code touches the real settings file -- fake this in tests, never the
/// real file system directly.
/// </summary>
public interface ISettingsStore
{
    /// <summary>Returns the stored settings, or an all-defaults <see cref="OrchestratorSettings"/>
    /// if nothing has been saved yet or the file can't be read (corrupt/missing) -- never
    /// throws for that reason, since missing settings just means "use the defaults."</summary>
    Task<OrchestratorSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(OrchestratorSettings settings, CancellationToken cancellationToken = default);
}
