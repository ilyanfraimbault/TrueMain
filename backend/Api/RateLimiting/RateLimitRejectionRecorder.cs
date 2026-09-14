using System.Collections.Concurrent;
using Data.Logging;

namespace TrueMain.RateLimiting;

/// <summary>
/// Turns rate-limit rejections into ops log rows an operator can count (#1555).
/// </summary>
/// <remarks>
/// <para>
/// Logging every 429 would defeat the point: a single client hammering the API
/// produces thousands a minute, and the diagnostic log channel is bounded — the
/// flood would evict the very errors worth reading. So each partition gets one
/// row the first time it is rejected in a window, carrying the request that
/// tripped the limit, and every rejection is counted.
/// <see cref="PublishWindow"/> then writes one row per partition that was
/// rejected more than once, with the exact count, capped at
/// <see cref="MaxPartitionRowsPerWindow"/> rows plus one row totalling the rest.
/// </para>
/// <para>
/// A rejection racing the window swap may be counted in the next window rather
/// than this one; the totals across windows stay exact.
/// </para>
/// </remarks>
public sealed class RateLimitRejectionRecorder(ILogger<RateLimitRejectionRecorder> logger)
{
    /// <summary>Most partitions given their own summary row per window; the rest share one.</summary>
    internal const int MaxPartitionRowsPerWindow = 50;

    private ConcurrentDictionary<string, int> _rejections = NewWindow();

    public void Record(string partition, string method, string path)
    {
        var rejections = Volatile.Read(ref _rejections);
        var count = rejections.AddOrUpdate(partition, 1, static (_, current) => current + 1);
        if (count == 1)
        {
            logger.LogWarning(
                OpsEvents.RateLimitRejected,
                "Rate limit reached for {Partition}: {RequestMethod} {RequestPath} answered {StatusCode}; further rejections in this window are counted",
                partition,
                method,
                path,
                StatusCodes.Status429TooManyRequests);
        }
    }

    public void PublishWindow(int windowSeconds)
    {
        var window = Interlocked.Exchange(ref _rejections, NewWindow());
        var repeated = window
            .Where(entry => entry.Value > 1)
            .OrderByDescending(entry => entry.Value)
            .ToList();

        foreach (var (partition, count) in repeated.Take(MaxPartitionRowsPerWindow))
        {
            logger.LogWarning(
                OpsEvents.RateLimitRejected,
                "Rate limit rejected {RejectedCount} requests from {Partition} in the last {WindowSeconds} s",
                count,
                partition,
                windowSeconds);
        }

        if (repeated.Count > MaxPartitionRowsPerWindow)
        {
            var rest = repeated.Skip(MaxPartitionRowsPerWindow).ToList();
            logger.LogWarning(
                OpsEvents.RateLimitRejected,
                "Rate limit rejected {RejectedCount} more requests from {PartitionCount} other partitions in the last {WindowSeconds} s",
                rest.Sum(entry => entry.Value),
                rest.Count,
                windowSeconds);
        }
    }

    private static ConcurrentDictionary<string, int> NewWindow() => new(StringComparer.Ordinal);
}
