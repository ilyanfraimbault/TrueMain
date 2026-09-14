using Microsoft.Extensions.Options;
using TrueMain.Options;

namespace TrueMain.RateLimiting;

/// <summary>
/// Closes a <see cref="RateLimitRejectionRecorder"/> window every
/// <c>RateLimit:WindowSeconds</c>, so the per-partition rejection counts reach the
/// ops logs on the limiter's own cadence (#1555). A final window is published on
/// shutdown so the last burst before a restart is not lost.
/// </summary>
public sealed class RateLimitRejectionSummaryService(
    RateLimitRejectionRecorder recorder,
    IOptions<RateLimitOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var windowSeconds = options.Value.WindowSeconds;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(windowSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                recorder.PublishWindow(windowSeconds);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down; publish what the current window counted below.
        }

        recorder.PublishWindow(windowSeconds);
    }
}
