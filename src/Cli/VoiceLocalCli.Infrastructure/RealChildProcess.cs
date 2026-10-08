using System.Diagnostics;

using VoiceLocalCli.Application.Ports;

namespace VoiceLocalCli.Infrastructure;

/// <summary>
/// Wraps a real <see cref="Process"/> with redirected stdio and no visible window. Output
/// is read line-by-line on background read loops and relayed through the
/// <see cref="IChildProcess"/> events -- the standard pattern for consuming a child
/// process's output without risking a full-pipe-buffer deadlock.
/// </summary>
internal sealed class RealChildProcess : IChildProcess
{
    private readonly Process _process;

    internal RealChildProcess(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                OutputLineReceived?.Invoke(e.Data);
            }
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                ErrorLineReceived?.Invoke(e.Data);
            }
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        ProcessJobObject.EnsureProcessIsTracked(_process);
    }

    public int Id => _process.Id;

    public bool HasExited => _process.HasExited;

    public event Action<string>? OutputLineReceived;

    public event Action<string>? ErrorLineReceived;

    public async Task WriteStandardInputAsync(string text, CancellationToken cancellationToken = default)
    {
        await _process.StandardInput.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        WaitForExitWithTimeoutAsync(timeout, cancellationToken);

    private async Task<bool> WaitForExitWithTimeoutAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await _process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return false;
        }
    }

    public void Kill() => _process.Kill(entireProcessTree: true);
}
