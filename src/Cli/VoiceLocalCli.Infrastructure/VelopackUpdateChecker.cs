using Velopack;
using Velopack.Sources;

using VoiceLocalCli.Application.Ports;
using VoiceLocalCli.Domain;

namespace VoiceLocalCli.Infrastructure;

/// <summary>
/// The real <see cref="IUpdateChecker"/> adapter: wraps Velopack's <see cref="UpdateManager"/>
/// against this repository's GitHub Releases. See
/// docs/adr/0004-velopack-for-auto-updates.md for why Velopack over a bespoke updater,
/// winget, or MSIX.
/// </summary>
public sealed class VelopackUpdateChecker : IUpdateChecker
{
    // Unauthenticated GitHub API access (null token) is rate-limited to 60 requests/hour
    // per IP -- irrelevant for one update check per app launch by one user.
    private const string RepositoryUrl = "https://github.com/ftrauernicht/voice-local-cli";

    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
    private UpdateInfo? _pendingUpdate;

    public bool IsInstalled => _manager.IsInstalled;

    public async Task<AvailableUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
        {
            return null;
        }

        try
        {
            _pendingUpdate = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // No network, no release published yet, GitHub unreachable -- never fatal, the
            // app works fully without ever finding an update. See IUpdateChecker's own doc
            // comment for why this is a deliberate, broad catch rather than a bug.
            _pendingUpdate = null;
        }

        return _pendingUpdate is null
            ? null
            : new AvailableUpdate(_pendingUpdate.TargetFullRelease.Version.ToString(), _pendingUpdate.TargetFullRelease.NotesMarkdown);
    }

    public async Task DownloadAndApplyUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (_pendingUpdate is null)
        {
            return;
        }

        await _manager.DownloadUpdatesAsync(_pendingUpdate, progress: null, cancellationToken).ConfigureAwait(false);
        _manager.ApplyUpdatesAndRestart(_pendingUpdate.TargetFullRelease, restartArgs: []);
    }
}
