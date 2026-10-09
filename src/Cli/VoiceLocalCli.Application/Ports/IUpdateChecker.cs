using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Application.Ports;

/// <summary>
/// Checks for and applies newer releases. The one port through which Application code
/// touches the real update mechanism (Velopack) -- fake this in tests, never the real
/// update library directly.
/// </summary>
public interface IUpdateChecker
{
    /// <summary>True only when running from a real installed copy (not <c>dotnet run</c>
    /// from a source checkout) -- there is nothing to check an update against otherwise.
    /// <see cref="CheckForUpdateAsync"/> always returns null when this is false, rather
    /// than every caller having to check both.</summary>
    bool IsInstalled { get; }

    /// <summary>The version of this running installed copy, or null when
    /// <see cref="IsInstalled"/> is false -- the authoritative source for "what version is
    /// this," read from Velopack's own install manifest rather than assembly metadata
    /// (which a self-contained single-file publish doesn't reliably carry the same way).
    /// Used to stamp/compare the installed-mode engine venv's bootstrap version -- see
    /// RepositoryLayout.StampVenvVersion.</summary>
    string? CurrentVersion { get; }

    /// <summary>Returns the newer release found, or null if already up to date, not
    /// installed, or the check itself failed (no network, no release published yet, GitHub
    /// unreachable) -- a failed check is never an error the caller has to handle, since the
    /// app works fully without ever finding an update.</summary>
    Task<AvailableUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads and applies the update found by the most recent
    /// <see cref="CheckForUpdateAsync"/> call, then restarts the app. Does nothing if no
    /// update was found by that call.</summary>
    Task DownloadAndApplyUpdateAsync(CancellationToken cancellationToken = default);
}
