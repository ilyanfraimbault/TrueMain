using Data.Logging;
using Microsoft.Extensions.Logging;
using TrueMain.RateLimiting;
using TrueMain.TestKit;

namespace TrueMain.UnitTests;

/// <summary>
/// 429s must reach the ops logs as something an operator can count, without a
/// flood of them evicting the rest of the bounded log channel (#1555).
/// </summary>
public class RateLimitRejectionRecorderTests
{
    private static object? Property(CapturedLog entry, string key)
        => entry.Properties.Single(property => property.Key == key).Value;

    [Fact]
    public void LogsOnlyTheFirstRejectionOfEachPartitionWithItsRequest()
    {
        var logger = new CapturingLogger<RateLimitRejectionRecorder>();
        var recorder = new RateLimitRejectionRecorder(logger);

        recorder.Record("203.0.113.7", "GET", "/champions/1");
        recorder.Record("203.0.113.7", "GET", "/champions/2");
        recorder.Record("203.0.113.7", "GET", "/champions/3");
        recorder.Record("198.51.100.4", "GET", "/truemains");

        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, entry => Assert.Equal(OpsEvents.RateLimitRejected, entry.EventId));
        var first = logger.Entries[0];
        Assert.Equal(LogLevel.Warning, first.Level);
        Assert.Equal("203.0.113.7", Property(first, "Partition"));
        Assert.Equal("/champions/1", Property(first, "RequestPath"));
        Assert.Equal(429, Property(first, "StatusCode"));
    }

    [Fact]
    public void PublishesTheCountOfEveryPartitionRejectedMoreThanOnce()
    {
        var logger = new CapturingLogger<RateLimitRejectionRecorder>();
        var recorder = new RateLimitRejectionRecorder(logger);
        recorder.Record("203.0.113.7", "GET", "/a");
        recorder.Record("203.0.113.7", "GET", "/b");
        recorder.Record("203.0.113.7", "GET", "/c");
        recorder.Record("198.51.100.4", "GET", "/d");
        logger.Entries.Clear();

        recorder.PublishWindow(60);

        // The single-rejection partition already has its row; repeating it adds nothing.
        var summary = Assert.Single(logger.Entries);
        Assert.Equal(OpsEvents.RateLimitRejected, summary.EventId);
        Assert.Equal(3, Property(summary, "RejectedCount"));
        Assert.Equal("203.0.113.7", Property(summary, "Partition"));
        Assert.Equal(60, Property(summary, "WindowSeconds"));
    }

    [Fact]
    public void StartsANewWindowAfterPublishing()
    {
        var logger = new CapturingLogger<RateLimitRejectionRecorder>();
        var recorder = new RateLimitRejectionRecorder(logger);
        recorder.Record("203.0.113.7", "GET", "/a");
        recorder.PublishWindow(60);
        logger.Entries.Clear();

        recorder.Record("203.0.113.7", "GET", "/b");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("/b", Property(entry, "RequestPath"));
    }

    [Fact]
    public void CapsThePerPartitionRowsAndTotalsTheRest()
    {
        var logger = new CapturingLogger<RateLimitRejectionRecorder>();
        var recorder = new RateLimitRejectionRecorder(logger);
        var partitions = RateLimitRejectionRecorder.MaxPartitionRowsPerWindow + 5;
        for (var partition = 0; partition < partitions; partition++)
        {
            recorder.Record($"10.0.0.{partition}", "GET", "/a");
            recorder.Record($"10.0.0.{partition}", "GET", "/a");
        }

        logger.Entries.Clear();

        recorder.PublishWindow(60);

        Assert.Equal(RateLimitRejectionRecorder.MaxPartitionRowsPerWindow + 1, logger.Entries.Count);
        var rest = logger.Entries[^1];
        Assert.Equal(10, Property(rest, "RejectedCount"));
        Assert.Equal(5, Property(rest, "PartitionCount"));
    }
}
