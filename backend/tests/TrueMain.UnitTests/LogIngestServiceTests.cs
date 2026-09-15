using Data.Logging;
using Data.Logging.Mongo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TrueMain.LogIngest;
using TrueMain.Requests.Internal;

namespace TrueMain.UnitTests;

/// <summary>
/// What a frontend's report becomes in the ops logs (#1556), and what it cannot become.
/// </summary>
public class LogIngestServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private sealed class RecordingWriter : IForwardedLogWriter
    {
        public List<ForwardedLogEntry> Entries { get; } = [];

        public bool TryWrite(ForwardedLogEntry entry)
        {
            Entries.Add(entry);
            return true;
        }
    }

    private static (LogIngestService Service, RecordingWriter Writer) Create()
    {
        var writer = new RecordingWriter();
        return (new LogIngestService(writer, new FakeTimeProvider(Now)), writer);
    }

    private static LogIngestRequest Request(params LogIngestEntry[] entries)
        => new() { Process = "Web", Host = "truemain-web", Entries = entries };

    private static LogIngestEntry Entry(DateTime? timestampUtc = null, string? eventType = "FrontendServerError", int? count = null)
        => new()
        {
            TimestampUtc = timestampUtc,
            Level = "Error",
            Category = "nitro",
            Message = "GET /champions/{n} failed",
            EventType = eventType,
            RequestMethod = "GET",
            RequestPath = "/champions/{n}",
            StatusCode = 502,
            Count = count
        };

    [Fact]
    public void StampsTheReportingProcessAndCarriesTheRequest()
    {
        var (service, writer) = Create();

        var result = service.Ingest(Request(Entry()));

        Assert.Equal(1, result.Accepted);
        var row = Assert.Single(writer.Entries);
        Assert.Equal("Web", row.ProcessName);
        Assert.Equal("truemain-web", row.Host);
        Assert.Equal(LogLevel.Error, row.Level);
        Assert.Equal(nameof(OpsEvents.FrontendServerError), row.EventType);
        Assert.Equal("/champions/{n}", row.RequestPath);
        Assert.Equal(502, row.StatusCode);
    }

    [Theory]
    [InlineData(nameof(OpsEvents.RequestFailed))]
    [InlineData(nameof(OpsEvents.ProcessRunFailed))]
    [InlineData("SomethingInvented")]
    public void RejectsTheWholeBatchForAnEventAFrontendMayNotReport(string eventType)
    {
        var (service, writer) = Create();

        var result = service.Ingest(Request(Entry(), Entry(eventType: eventType)));

        Assert.Equal(0, result.Accepted);
        Assert.True(result.Errors.ContainsKey("entries[1].eventType"));
        Assert.Empty(writer.Entries);
    }

    [Theory]
    [InlineData(-11)]
    [InlineData(2)]
    public void ReplacesAnImplausibleTimestampWithTheServerClock(int minutesFromNow)
    {
        var (service, writer) = Create();

        service.Ingest(Request(Entry(timestampUtc: Now.UtcDateTime.AddMinutes(minutesFromNow))));

        Assert.Equal(Now.UtcDateTime, Assert.Single(writer.Entries).TimestampUtc);
    }

    [Fact]
    public void KeepsARecentTimestamp()
    {
        var (service, writer) = Create();
        var reported = Now.UtcDateTime.AddMinutes(-3);

        service.Ingest(Request(Entry(timestampUtc: reported)));

        Assert.Equal(reported, Assert.Single(writer.Entries).TimestampUtc);
    }

    [Fact]
    public void FoldsTheOccurrenceCountIntoTheMessage()
    {
        var (service, writer) = Create();

        service.Ingest(Request(Entry(count: 42)));

        Assert.Equal("GET /champions/{n} failed (42 occurrences since the previous report)", Assert.Single(writer.Entries).Message);
    }
}
