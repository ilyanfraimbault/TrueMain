using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Logging;
using Data.Logging.Mongo;
using MongoDB.Driver;

namespace TrueMain.IntegrationTests;

/// <summary>
/// <c>POST /internal/logs</c> end to end (#1556): who may call it, what it refuses, and
/// the row an accepted report becomes.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class LogIngestApiIntegrationTests
{
    private const string IngestKey = "test-log-ingest-key-0123456789-abcdefghijkl";
    private readonly PostgresFixture _postgres;
    private readonly MongoFixture _mongo;

    public LogIngestApiIntegrationTests(PostgresFixture postgres, MongoFixture mongo)
    {
        _postgres = postgres;
        _mongo = mongo;
    }

    private TrueMainWebApplicationFactory<Program> CreateFactory(string ingestKey = IngestKey)
        => new(
            _postgres,
            [
                new KeyValuePair<string, string?>("MongoLogging:ConnectionString", _mongo.ConnectionString),
                new KeyValuePair<string, string?>("MongoLogging:Database", MongoFixture.DatabaseName),
                new KeyValuePair<string, string?>("MongoLogging:LogsCollection", MongoFixture.LogsCollection),
                new KeyValuePair<string, string?>("MongoLogging:AuditCollection", MongoFixture.AuditCollection),
                new KeyValuePair<string, string?>("MongoLogging:MinimumLevel", "Warning"),
                new KeyValuePair<string, string?>("MongoLogging:FlushInterval", "00:00:00.100"),
                new KeyValuePair<string, string?>("LogIngest:ApiKey", ingestKey)
            ]);

    private static object Report(string process = "Web", object[]? entries = null)
        => new
        {
            process,
            host = "truemain-web",
            entries = entries ??
            [
                new
                {
                    level = "Error",
                    category = "nitro",
                    message = "GET /api/champions/{n} could not reach the API",
                    eventType = "FrontendServerError",
                    requestMethod = "GET",
                    requestPath = "/api/champions/{n}",
                    statusCode = 502,
                    count = 3
                }
            ]
        };

    private static HttpRequestMessage Post(object body, string? headerName, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/logs") { Content = JsonContent.Create(body) };
        if (headerName is not null && key is not null)
        {
            request.Headers.Add(headerName, key);
        }

        return request;
    }

    [Fact]
    public async Task AcceptedReport_IsPersistedUnderTheFrontendProcess()
    {
        await _mongo.ResetAsync();
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var request = Post(Report(), "X-Log-Ingest-Key", IngestKey);
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var collection = _mongo.GetCollection<MongoLogDocument>(MongoFixture.LogsCollection);
        var forwarded = Builders<MongoLogDocument>.Filter.Eq(doc => doc.ProcessName, "Web");
        await AsyncWait.UntilAsync(
            async () => await collection.CountDocumentsAsync(forwarded) == 1,
            "the forwarded row to reach the logs collection");

        var row = await collection.Find(forwarded).SingleAsync();
        row.Level.Should().Be("Error");
        row.EventType.Should().Be(nameof(OpsEvents.FrontendServerError));
        row.EventId.Should().Be(OpsEvents.FrontendServerError.Id);
        row.Host.Should().Be("truemain-web");
        row.RequestPath.Should().Be("/api/champions/{n}");
        row.StatusCode.Should().Be(502);
        row.Message.Should().EndWith("(3 occurrences since the previous report)");
    }

    [Fact]
    public async Task Report_IsRefusedWithoutTheIngestKey()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var noKeyRequest = Post(Report(), null, null);
        using var noKey = await client.SendAsync(noKeyRequest);
        using var wrongKeyRequest = Post(Report(), "X-Log-Ingest-Key", IngestKey + "-wrong");
        using var wrongKey = await client.SendAsync(wrongKeyRequest);
        // The ops key opens /ops, not this.
        using var opsKeyRequest = Post(Report(), "X-Ops-Key", TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey);
        using var opsKey = await client.SendAsync(opsKeyRequest);

        noKey.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrongKey.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        opsKey.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Report_IsRefusedWhenIngestionIsOff()
    {
        await using var factory = CreateFactory(ingestKey: string.Empty);
        using var client = factory.CreateClient();

        using var request = Post(Report(), "X-Log-Ingest-Key", IngestKey);
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InvalidReports_AreRejectedWhole()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var entry = new { level = "Error", category = "nitro", message = "boom" };

        object[] invalid =
        [
            Report(process: "Api"),
            Report(entries: [new { level = "Information", category = "nitro", message = "not a signal" }]),
            Report(entries: [new { level = "Error", category = "nitro", message = "impersonation", eventType = "ProcessRunFailed" }]),
            Report(entries: Enumerable.Range(0, LogIngestRequestLimits.TooManyEntries).Select(_ => (object)entry).ToArray()),
            Report(entries: [])
        ];

        foreach (var body in invalid)
        {
            using var request = Post(body, "X-Log-Ingest-Key", IngestKey);
            using var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    private static class LogIngestRequestLimits
    {
        public const int TooManyEntries = TrueMain.Requests.Internal.LogIngestRequest.MaxEntries + 1;
    }
}
