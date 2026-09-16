using Data.Logging.Mongo;
using Microsoft.Extensions.Logging;

namespace TrueMain.UnitTests;

/// <summary>
/// A log row written during a request carries that request (#1555): from the
/// template's own properties, or from the hosting scope around it.
/// </summary>
public class MongoLogRequestFieldsTests
{
    [Fact]
    public void ReadsTheFieldsATemplateNames()
    {
        var state = new Dictionary<string, object?>
        {
            ["RequestMethod"] = "GET",
            ["RequestPath"] = "/champions/103",
            ["StatusCode"] = 503,
            ["DurationMs"] = 1234L,
            ["TraceId"] = "0HN7:00000001",
            ["{OriginalFormat}"] = "ignored"
        };

        var fields = MongoLogRequestFields.Extract(state, scopes: null);

        Assert.Equal(new MongoLogRequestFields("0HN7:00000001", "GET", "/champions/103", 503, 1234L), fields);
    }

    [Fact]
    public void ReadsTheHostingScopeAroundARecordThatNamesNothing()
    {
        var scopes = new LoggerExternalScopeProvider();
        using var hosting = scopes.Push(new Dictionary<string, object?>
        {
            ["RequestId"] = "0HN7:00000002",
            ["RequestPath"] = "/truemains"
        });
        // The activity scope's W3C trace id is not the id the client was handed.
        using var activity = scopes.Push(new Dictionary<string, object?> { ["TraceId"] = "4bf92f3577b34da6a3ce929d0e0e4736" });

        var fields = MongoLogRequestFields.Extract("An unhandled exception has occurred.", scopes);

        Assert.Equal("0HN7:00000002", fields.TraceId);
        Assert.Equal("/truemains", fields.RequestPath);
        Assert.Null(fields.StatusCode);
    }

    [Fact]
    public void LetsTheTemplateWinOverTheScope()
    {
        var scopes = new LoggerExternalScopeProvider();
        using var hosting = scopes.Push(new Dictionary<string, object?> { ["RequestPath"] = "/from-scope" });

        var fields = MongoLogRequestFields.Extract(
            new Dictionary<string, object?> { ["RequestPath"] = "/from-state" },
            scopes);

        Assert.Equal("/from-state", fields.RequestPath);
    }

    [Fact]
    public void LeavesEverythingNullOutsideARequest()
        => Assert.Equal(default, MongoLogRequestFields.Extract("plain message", new LoggerExternalScopeProvider()));
}

/// <summary>
/// The bounded log channel still drops under pressure, but it now counts what it
/// dropped, so the sink can say so (#1555).
/// </summary>
public class MongoLogChannelTests
{
    private static MongoLogRecord Record(string message)
        => new(DateTime.UtcNow, LogLevel.Warning, "Test", 0, message, null, "Api", "host", null);

    [Fact]
    public void CountsTheRecordsItEvictsAndResetsOnRead()
    {
        var channel = new MongoLogChannel(Microsoft.Extensions.Options.Options.Create(new MongoLoggingOptions { Capacity = 2 }));

        for (var index = 0; index < 5; index++)
        {
            channel.TryWrite(Record($"record {index}"));
        }

        Assert.Equal(3, channel.TakeDroppedCount());
        Assert.Equal(0, channel.TakeDroppedCount());
        Assert.True(channel.Reader.TryRead(out var oldestKept));
        Assert.Equal("record 3", oldestKept.Message);
    }
}
