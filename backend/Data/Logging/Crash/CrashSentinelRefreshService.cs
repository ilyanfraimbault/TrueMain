using Data.Logging.Mongo;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Data.Logging.Crash;

/// <summary>
/// Periodically refreshes the crash sentinel's <c>lastSeenUtc</c> and live memory
/// snapshot while the process runs. That snapshot is what an
/// <see cref="CrashSource.UncleanShutdown"/> report carries on the next boot, so a
/// process OOM-killed mid-run leaves behind its rising working set / Gen2 count — the
/// strongest available "this was probably OOM" signal.
/// </summary>
internal sealed class CrashSentinelRefreshService(IOptions<MongoLoggingOptions> options) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly MongoLoggingOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var processName = CrashSentinel.ResolveProcessName(_options);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                CrashSentinel.Refresh(_options, processName);
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown; ApplicationStopping flips the sentinel to "stopped".
        }
    }
}
