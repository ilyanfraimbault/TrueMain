using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Data.Logging;
using Data.Logging.Mongo;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Verifies the <c>GET /ops/logs</c> contract against the MongoDB-backed log
/// store (logs moved off Postgres in #416). The response shape and filter
/// semantics are unchanged from the Postgres implementation, so the admin viewer
/// keeps working without a frontend change.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class LogsApiIntegrationTests
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _postgres;
    private readonly MongoFixture _mongo;

    public LogsApiIntegrationTests(PostgresFixture postgres, MongoFixture mongo)
    {
        _postgres = postgres;
        _mongo = mongo;
    }

    [Fact]
    public async Task GetLogsAsync_ShouldReturnNewestFirstWithStableShape()
    {
        await ResetAsync();
        await SeedLogsAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/ops/logs");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["entries", "total", "page", "pageSize", "eventTypes", "processes"]);

        var firstEntry = document.RootElement.GetProperty("entries")[0];
        firstEntry.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(
            [
                "id", "timestampUtc", "level", "category",
                "message", "exception", "processName", "host", "eventType"
            ]);

        var payload = await response.Content.ReadFromJsonAsync<LogsTestContract>();
        payload.Should().NotBeNull();

        // Five documents seeded; the diagnostic sink is muted in the test host
        // (MinimumLevel=None) so the count is exactly the seed.
        payload!.Total.Should().Be(5);
        payload.Page.Should().Be(1);
        payload.PageSize.Should().Be(50);
        payload.Entries.Should().HaveCount(5);
        payload.Entries.Should().BeInDescendingOrder(entry => entry.TimestampUtc);
        // Newest seeded row is the Critical one.
        payload.Entries[0].Level.Should().Be("Critical");
        payload.Entries[0].Message.Should().Be("database connection lost");
        // The id is the Mongo ObjectId surfaced as a 24-char hex string.
        payload.Entries[0].Id.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetLogsAsync_ShouldFilterByMinimumLevel()
    {
        await ResetAsync();
        await SeedLogsAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // level=Error is a minimum threshold: Error + Critical, not the two
        // Warning rows and not Information.
        var payload = await client.GetFromJsonAsync<LogsTestContract>("/ops/logs?level=Error");
        payload.Should().NotBeNull();

        payload!.Total.Should().Be(2);
        payload.Entries.Should().OnlyContain(entry => entry.Level == "Error" || entry.Level == "Critical");
    }

    [Fact]
    public async Task GetLogsAsync_ShouldSearchMessageAndExceptionCaseInsensitively()
    {
        await ResetAsync();
        await SeedLogsAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // "timeout" appears in one message; the search is a case-insensitive regex
        // and also scans the exception text, where "TimeoutException" lives on a
        // different (Error) row — so both rows match.
        var payload = await client.GetFromJsonAsync<LogsTestContract>("/ops/logs?search=TIMEOUT");
        payload.Should().NotBeNull();

        payload!.Total.Should().Be(2);
        payload.Entries.Select(entry => entry.Message)
            .Should().BeEquivalentTo(["riot api timeout", "ingest failed"]);
    }

    [Fact]
    public async Task GetLogsAsync_ShouldFilterByCategoryPrefixCaseInsensitively()
    {
        await ResetAsync();
        await SeedLogsAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // Prefix match, case-insensitive: "ingestor" matches the two
        // "TrueMain.Ingestor.*" warning categories (not the "Ingestor.Worker" one,
        // which does not share the prefix).
        var payload = await client.GetFromJsonAsync<LogsTestContract>("/ops/logs?category=TrueMain.Ingestor");
        payload.Should().NotBeNull();

        payload!.Total.Should().Be(2);
        payload.Entries.Should().OnlyContain(entry => entry.Category.StartsWith("TrueMain.Ingestor"));
    }

    [Fact]
    public async Task GetLogsAsync_ShouldFilterBySinceExcludingOlderEntries()
    {
        await ResetAsync();
        await SeedLogsAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // The seed spans now-50m .. now-10m. A 25-minute cutoff sits between the
        // -30m row (excluded) and the -20m row (included), with a wide margin on
        // both sides so the millisecond drift between the seed's and this test's
        // DateTime.UtcNow can never flip a row. since is an inclusive lower bound
        // (TimestampUtc >= since), so only the two newest rows survive.
        var since = DateTime.UtcNow.AddMinutes(-25);
        var sinceQuery = Uri.EscapeDataString(since.ToString("o"));

        var payload = await client.GetFromJsonAsync<LogsTestContract>($"/ops/logs?since={sinceQuery}");
        payload.Should().NotBeNull();

        // Only the Error (-20m) and Critical (-10m) rows are newer than the cutoff;
        // the three older rows (-50m, -40m, -30m) are filtered out.
        payload!.Total.Should().Be(2);
        payload.Entries.Should().HaveCount(2);
        payload.Entries.Should().OnlyContain(entry => entry.TimestampUtc >= since);
        payload.Entries.Select(entry => entry.Level)
            .Should().BeEquivalentTo(["Critical", "Error"]);
    }

    [Fact]
    public async Task GetLogsAsync_ShouldPageAndReportUnpagedTotal()
    {
        await ResetAsync();
        await SeedLogsAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var firstPage = await client.GetFromJsonAsync<LogsTestContract>("/ops/logs?page=1&pageSize=2");
        var secondPage = await client.GetFromJsonAsync<LogsTestContract>("/ops/logs?page=2&pageSize=2");
        firstPage.Should().NotBeNull();
        secondPage.Should().NotBeNull();

        // Total is the unpaged match count on every page.
        firstPage!.Total.Should().Be(5);
        secondPage!.Total.Should().Be(5);
        firstPage.PageSize.Should().Be(2);
        firstPage.Entries.Should().HaveCount(2);
        secondPage.Entries.Should().HaveCount(2);

        // Pages are contiguous slices of the same newest-first ordering.
        var combined = firstPage.Entries.Concat(secondPage.Entries).ToList();
        combined.Should().BeInDescendingOrder(entry => entry.TimestampUtc);
        combined.Select(entry => entry.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task GetLogsAsync_ShouldFilterByEventTypeAndExposeKnownEventTypes()
    {
        await ResetAsync();
        await SeedLogsAsync();

        // Two named domain events among the plain diagnostics (#444). The filter
        // must return exactly the matching event rows — case-insensitively — and
        // never the plain rows, even when their levels would match.
        var collection = _mongo.GetCollection<MongoLogDocument>(MongoFixture.LogsCollection);
        await collection.InsertManyAsync(
        [
            BuildDocument(
                "Information",
                "Ingestor.Processes.ManualSeedProcess",
                "seed request resolved",
                null,
                DateTime.UtcNow.AddMinutes(-5),
                eventType: "SeedRequestResolved"),
            BuildDocument(
                "Information",
                "Ingestor.Processes.Components.MatchIngestion.AccountValidationService",
                "validated 2 candidates",
                null,
                DateTime.UtcNow.AddMinutes(-4),
                eventType: "CandidateValidated")
        ]);

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // Lowercase on purpose: eventType is an exact match but case-insensitive.
        var payload = await client.GetFromJsonAsync<LogsTestContract>("/ops/logs?eventType=seedrequestresolved");
        payload.Should().NotBeNull();

        payload!.Total.Should().Be(1);
        var entry = payload.Entries.Should().ContainSingle().Subject;
        entry.EventType.Should().Be("SeedRequestResolved");
        entry.Message.Should().Be("seed request resolved");

        // The known-event catalog rides on every response (static OpsEvents list,
        // no Mongo distinct) so the admin UI can build its filter select.
        payload.EventTypes.Should().BeEquivalentTo(OpsEvents.KnownEventTypes);

        // Plain diagnostic rows keep a null eventType.
        var unfiltered = await client.GetFromJsonAsync<LogsTestContract>("/ops/logs");
        unfiltered!.Total.Should().Be(7);
        unfiltered.Entries.Where(e => e.EventType is not null).Should().HaveCount(2);
    }

    [Fact]
    public async Task GetLogsAsync_ShouldFilterByProcessCaseInsensitivelyAndExposeCatalog()
    {
        await ResetAsync();
        await SeedLogsAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // Only the "Ingestor.Worker" row is stamped Ingestor by the seed; the
        // filter is an exact match resolved case-insensitively against the
        // static catalog (#722).
        var payload = await client.GetFromJsonAsync<LogsTestContract>("/ops/logs?process=ingestor");
        payload.Should().NotBeNull();

        payload!.Total.Should().Be(1);
        var entry = payload.Entries.Should().ContainSingle().Subject;
        entry.ProcessName.Should().Be("Ingestor");
        entry.Message.Should().Be("ingest failed");

        // The process catalog rides on every response (static list, no Mongo
        // distinct) so the admin UI can build its filter select.
        payload.Processes.Should().BeEquivalentTo(["Api", "Ingestor"]);
    }

    [Fact]
    public async Task GetLogsAsync_ShouldFilterToExceptionCarryingRows()
    {
        await ResetAsync();
        await SeedLogsAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // hasException=true keeps only rows with a formatted exception — one in
        // the seed; hasException=false is a no-op (same as omitting it).
        var filtered = await client.GetFromJsonAsync<LogsTestContract>("/ops/logs?hasException=true");
        filtered.Should().NotBeNull();
        filtered!.Total.Should().Be(1);
        filtered.Entries.Should().ContainSingle().Which.Exception.Should().Contain("TimeoutException");

        var unfiltered = await client.GetFromJsonAsync<LogsTestContract>("/ops/logs?hasException=false");
        unfiltered!.Total.Should().Be(5);
    }

    [Fact]
    public async Task GetLogsAsync_ShouldClampPageSizeToCeiling()
    {
        await ResetAsync();
        await SeedLogsAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // Above the 200 ceiling clamps to 200.
        var payload = await client.GetFromJsonAsync<LogsTestContract>("/ops/logs?pageSize=10000");
        payload.Should().NotBeNull();
        payload!.PageSize.Should().Be(200);
    }

    [Fact]
    public async Task GetLogsAsync_ShouldRequireOpsApiKey()
    {
        await ResetAsync();

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/logs");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task ResetAsync()
    {
        await _postgres.ResetDatabaseAsync();
        await _mongo.ResetAsync();
    }

    private ApiWebApplicationFactory CreateFactory() => new(_postgres, _mongo);

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }

    private async Task SeedLogsAsync()
    {
        var now = DateTime.UtcNow;
        var collection = _mongo.GetCollection<MongoLogDocument>(MongoFixture.LogsCollection);

        await collection.InsertManyAsync(
        [
            // Oldest -> newest by timestamp. Levels span the threshold boundary so
            // a level=Error filter excludes the two Warning rows below.
            BuildDocument("Information", "TrueMain.Api", "warmup complete", null, now.AddMinutes(-50)),
            BuildDocument("Warning", "TrueMain.Ingestor.Discovery", "no platforms configured", null, now.AddMinutes(-40)),
            BuildDocument("Warning", "TrueMain.Ingestor.MatchIngestion", "riot api timeout", null, now.AddMinutes(-30)),
            BuildDocument(
                "Error",
                "Ingestor.Worker",
                "ingest failed",
                "System.TimeoutException: The operation timed out.",
                now.AddMinutes(-20)),
            BuildDocument("Critical", "TrueMain.Api", "database connection lost", null, now.AddMinutes(-10))
        ]);
    }

    private static MongoLogDocument BuildDocument(
        string level,
        string category,
        string message,
        string? exception,
        DateTime timestampUtc,
        string? eventType = null)
        => new()
        {
            Level = level,
            Category = category,
            Message = message,
            Exception = exception,
            ProcessName = category.StartsWith("Ingestor", StringComparison.Ordinal) ? "Ingestor" : "Api",
            Host = "test-host",
            TimestampUtc = timestampUtc,
            EventType = eventType
        };

    // Point the host at the test Mongo container and mute the diagnostic sink
    // (MinimumLevel=None) so incidental host warnings never write extra log
    // documents; the seeded counts then stay deterministic. The store stays
    // active (Enabled + connection string), so the read path serves the seed.
    private sealed class ApiWebApplicationFactory(PostgresFixture postgres, MongoFixture mongo)
        : TrueMainWebApplicationFactory<Program>(
            postgres,
            [
                new KeyValuePair<string, string?>("MongoLogging:ConnectionString", mongo.ConnectionString),
                new KeyValuePair<string, string?>("MongoLogging:Database", MongoFixture.DatabaseName),
                new KeyValuePair<string, string?>("MongoLogging:LogsCollection", MongoFixture.LogsCollection),
                new KeyValuePair<string, string?>("MongoLogging:AuditCollection", MongoFixture.AuditCollection),
                new KeyValuePair<string, string?>("MongoLogging:MinimumLevel", "None")
            ]);

    private sealed class LogsTestContract
    {
        public IReadOnlyList<LogEntryTestContract> Entries { get; init; } = [];

        public long Total { get; init; }

        public int Page { get; init; }

        public int PageSize { get; init; }

        public IReadOnlyList<string> EventTypes { get; init; } = [];

        public IReadOnlyList<string> Processes { get; init; } = [];
    }

    private sealed class LogEntryTestContract
    {
        public string Id { get; init; } = string.Empty;

        public DateTime TimestampUtc { get; init; }

        public string Level { get; init; } = string.Empty;

        public string Category { get; init; } = string.Empty;

        public string Message { get; init; } = string.Empty;

        public string? Exception { get; init; }

        public string? ProcessName { get; init; }

        public string? Host { get; init; }

        public string? EventType { get; init; }
    }
}
