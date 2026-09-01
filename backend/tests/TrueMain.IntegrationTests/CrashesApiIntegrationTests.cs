using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Data.Logging.Crash;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Contract cover for <c>GET /ops/crashes</c> against the real Mongo-backed
/// <c>crashes</c> collection. The panel renders a crash entirely from the list payload —
/// there is no detail call — so what has to hold end to end is that the whole report
/// survives the trip: the exception chain, the environment/memory snapshot, the buffered
/// log tail, and the derived explanation.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class CrashesApiIntegrationTests
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;

    private readonly PostgresFixture _postgres;
    private readonly MongoFixture _mongo;

    public CrashesApiIntegrationTests(PostgresFixture postgres, MongoFixture mongo)
    {
        _postgres = postgres;
        _mongo = mongo;
    }

    [Fact]
    public async Task Requires_the_ops_api_key()
    {
        await ResetAsync();

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/crashes");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Returns_crashes_newest_first_with_a_stable_shape_and_its_filter_catalogs()
    {
        await ResetAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/ops/crashes");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["entries", "total", "page", "pageSize", "sources", "processes"]);

        document.RootElement.GetProperty("entries")[0].EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(
            [
                "id", "timestampUtc", "processName", "source", "explanation",
                "exceptionType", "message", "stackTrace", "innerExceptions",
                "host", "osDescription", "uptimeSeconds", "runtimeVersion", "appVersion",
                "workingSetBytes", "totalManagedMemoryBytes",
                "gen0Collections", "gen1Collections", "gen2Collections",
                "exitCode", "recentLogTail"
            ]);

        var payload = await response.Content.ReadFromJsonAsync<CrashesTestContract>();
        payload.Should().NotBeNull();

        payload!.Total.Should().Be(4);
        payload.Page.Should().Be(1);
        payload.PageSize.Should().Be(25);
        payload.Entries.Should().HaveCount(4);
        payload.Entries.Should().BeInDescendingOrder(entry => entry.TimestampUtc);
        payload.Entries[0].Message.Should().Be("Connection refused by riot api");
        payload.Entries.Select(entry => entry.Id).Should().OnlyHaveUniqueItems();

        // Static catalogs ride on every response so the panel can build its selects
        // without a Mongo distinct — and so a newly added CrashSource appears in the
        // filter the moment it exists.
        payload.Sources.Should().BeEquivalentTo(Enum.GetNames<CrashSource>());
        payload.Processes.Should().BeEquivalentTo(["Api", "Ingestor"]);
    }

    [Fact]
    public async Task Filters_by_process()
    {
        await ResetAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var payload = await client.GetFromJsonAsync<CrashesTestContract>("/ops/crashes?process=Ingestor");

        payload!.Total.Should().Be(2);
        payload.Entries.Should().OnlyContain(entry => entry.ProcessName == "Ingestor");
    }

    [Fact]
    public async Task Filters_by_source_case_insensitively_and_returns_nothing_for_an_unknown_one()
    {
        await ResetAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // Lowercase on purpose: the casing is resolved against the CrashSource catalog so
        // the filter stays an indexable $eq.
        var unclean = await client.GetFromJsonAsync<CrashesTestContract>("/ops/crashes?source=uncleanshutdown");
        unclean!.Total.Should().Be(1);
        unclean.Entries.Should().ContainSingle().Which.Source.Should().Be(nameof(CrashSource.UncleanShutdown));

        // A value outside the catalog falls through as-is and matches nothing — it must
        // never widen to "no filter", which would show every crash under a wrong heading.
        var nonsense = await client.GetFromJsonAsync<CrashesTestContract>("/ops/crashes?source=Meltdown");
        nonsense!.Total.Should().Be(0);
        nonsense.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Searches_message_and_stack_trace_case_insensitively_with_literal_metacharacters()
    {
        await ResetAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // "npgsql" appears in one message and, on a different row, only in the stack
        // trace — both must come back.
        var payload = await client.GetFromJsonAsync<CrashesTestContract>("/ops/crashes?search=NPGSQL");
        payload!.Total.Should().Be(2);

        // Regex metacharacters are escaped, so this is a substring search and not a
        // wildcard that matches every row.
        var literal = await client.GetFromJsonAsync<CrashesTestContract>("/ops/crashes?search=.%2A");
        literal!.Total.Should().Be(0);
    }

    [Fact]
    public async Task Treats_since_as_an_inclusive_lower_bound()
    {
        await ResetAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // The seed spans now-50m .. now-5m. A 25-minute cutoff sits between the -30m row
        // (excluded) and the -20m row (included), with wide margin on both sides so
        // millisecond drift between the seed's clock and this one can never flip a row.
        var since = DateTime.UtcNow.AddMinutes(-25);
        var payload = await client.GetFromJsonAsync<CrashesTestContract>(
            $"/ops/crashes?since={Uri.EscapeDataString(since.ToString("o"))}");

        payload!.Total.Should().Be(2);
        payload.Entries.Should().OnlyContain(entry => entry.TimestampUtc >= since);
    }

    [Fact]
    public async Task Pages_the_list_while_reporting_the_unpaged_total_and_clamping_the_page_size()
    {
        await ResetAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var first = await client.GetFromJsonAsync<CrashesTestContract>("/ops/crashes?page=1&pageSize=2");
        var second = await client.GetFromJsonAsync<CrashesTestContract>("/ops/crashes?page=2&pageSize=2");

        first!.Total.Should().Be(4, "the total is the unpaged match count on every page");
        second!.Total.Should().Be(4);
        first.Entries.Should().HaveCount(2);
        second.Entries.Should().HaveCount(2);

        // Contiguous slices of one newest-first ordering, with no row served twice.
        var combined = first.Entries.Concat(second.Entries).ToList();
        combined.Should().BeInDescendingOrder(entry => entry.TimestampUtc);
        combined.Select(entry => entry.Id).Should().OnlyHaveUniqueItems();

        var oversized = await client.GetFromJsonAsync<CrashesTestContract>("/ops/crashes?pageSize=10000");
        oversized!.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task Carries_the_whole_report_so_the_panel_needs_no_detail_call()
    {
        await ResetAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var payload = await client.GetFromJsonAsync<CrashesTestContract>("/ops/crashes?source=HostRun");
        var entry = payload!.Entries.Should().ContainSingle().Subject;

        entry.ExceptionType.Should().Be("System.Net.Sockets.SocketException");
        entry.StackTrace.Should().Contain("Npgsql");
        entry.InnerExceptions.Should().ContainSingle()
            .Which.Type.Should().Be("System.TimeoutException");
        entry.Host.Should().Be("api-1");
        entry.OsDescription.Should().Be("Linux 6.1");
        entry.RuntimeVersion.Should().Be("10.0.0");
        entry.AppVersion.Should().Be("1.2.3");
        entry.UptimeSeconds.Should().Be(4242d);
        entry.Gen2Collections.Should().Be(3);

        // The buffered log lines that preceded the crash, oldest-first — the reason the
        // panel can explain a death without cross-referencing the log viewer.
        entry.RecentLogTail.Should().HaveCount(2);
        entry.RecentLogTail[0].Message.Should().Be("starting ingest");
        entry.RecentLogTail[1].Level.Should().Be("Error");
    }

    [Fact]
    public async Task Explains_an_unclean_shutdown_that_carries_no_exception_at_all()
    {
        await ResetAsync();
        await SeedAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var payload = await client.GetFromJsonAsync<CrashesTestContract>("/ops/crashes?source=UncleanShutdown");
        var entry = payload!.Entries.Should().ContainSingle().Subject;

        // No stack trace is possible for a SIGKILL; the exit code and the last-known
        // memory snapshot are the whole diagnosis, and the derived explanation is what
        // the operator actually reads.
        entry.ExceptionType.Should().BeNull();
        entry.StackTrace.Should().BeNull();
        entry.InnerExceptions.Should().BeEmpty();
        entry.ExitCode.Should().Be(137);
        entry.WorkingSetBytes.Should().Be(3L * 1024 * 1024 * 1024);
        entry.Explanation.Should().Contain("out-of-memory");
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

    private async Task SeedAsync()
    {
        var now = DateTime.UtcNow;
        var collection = _mongo.GetCollection<CrashReportDocument>(MongoFixture.CrashesCollection);

        await collection.InsertManyAsync(
        [
            // Oldest -> newest. The two Ingestor rows and the two Api rows straddle the
            // -25m cutoff the `since` test uses.
            new CrashReportDocument
            {
                ReportId = Guid.NewGuid().ToString(),
                TimestampUtc = now.AddMinutes(-50),
                ProcessName = "Ingestor",
                Source = nameof(CrashSource.UncleanShutdown),
                Host = "ingestor-1",
                OsDescription = "Linux 6.1",
                UptimeSeconds = 90_000,
                RuntimeVersion = "10.0.0",
                AppVersion = "1.2.3",
                WorkingSetBytes = 3L * 1024 * 1024 * 1024,
                TotalManagedMemoryBytes = 2L * 1024 * 1024 * 1024,
                Gen0Collections = 900,
                Gen1Collections = 300,
                Gen2Collections = 30,
                ExitCode = 137
            },
            new CrashReportDocument
            {
                ReportId = Guid.NewGuid().ToString(),
                TimestampUtc = now.AddMinutes(-30),
                ProcessName = "Ingestor",
                Source = nameof(CrashSource.TaskSchedulerUnobserved),
                Message = "A task faulted while writing to Npgsql",
                ExceptionType = "System.InvalidOperationException",
                StackTrace = "   at Ingestor.Processes.HarvestProcess.RunAsync()",
                Host = "ingestor-1",
                UptimeSeconds = 120
            },
            new CrashReportDocument
            {
                ReportId = Guid.NewGuid().ToString(),
                TimestampUtc = now.AddMinutes(-20),
                ProcessName = "Api",
                Source = nameof(CrashSource.AppDomainUnhandled),
                Message = "Object reference not set",
                ExceptionType = "System.NullReferenceException",
                StackTrace = "   at TrueMain.Controllers.Ops.OpsController.GetCrashesAsync()",
                Host = "api-1",
                UptimeSeconds = 10
            },
            new CrashReportDocument
            {
                ReportId = Guid.NewGuid().ToString(),
                TimestampUtc = now.AddMinutes(-5),
                ProcessName = "Api",
                Source = nameof(CrashSource.HostRun),
                Message = "Connection refused by riot api",
                ExceptionType = "System.Net.Sockets.SocketException",
                // "Npgsql" only in the stack trace here, so the search test proves both
                // fields are scanned.
                StackTrace = "   at Npgsql.NpgsqlConnection.Open()",
                InnerExceptions =
                [
                    new CrashExceptionDocument
                    {
                        Type = "System.TimeoutException",
                        Message = "The operation timed out.",
                        StackTrace = "   at Npgsql.Internal.NpgsqlConnector.Connect()"
                    }
                ],
                Host = "api-1",
                OsDescription = "Linux 6.1",
                UptimeSeconds = 4242,
                RuntimeVersion = "10.0.0",
                AppVersion = "1.2.3",
                WorkingSetBytes = 512L * 1024 * 1024,
                TotalManagedMemoryBytes = 256L * 1024 * 1024,
                Gen0Collections = 12,
                Gen1Collections = 5,
                Gen2Collections = 3,
                RecentLogTail =
                [
                    new CrashLogTailDocument
                    {
                        TimestampUtc = now.AddMinutes(-6),
                        Level = "Information",
                        Category = "TrueMain.Api",
                        Message = "starting ingest"
                    },
                    new CrashLogTailDocument
                    {
                        TimestampUtc = now.AddMinutes(-5).AddSeconds(-1),
                        Level = "Error",
                        Category = "TrueMain.Api",
                        Message = "riot api unreachable",
                        Exception = "System.Net.Sockets.SocketException"
                    }
                ]
            }
        ]);
    }

    /// <summary>
    /// Points the host at the test Mongo container, mutes the diagnostic sink so incidental
    /// host warnings never write extra documents, and — the part specific to this suite —
    /// gives crash reporting a fresh sentinel directory per factory. The sentinel is how a
    /// boot detects the *previous* run's unclean death; left on a shared path, a stale one
    /// would make the host write a real crash report into the seeded collection.
    /// </summary>
    private sealed class ApiWebApplicationFactory(PostgresFixture postgres, MongoFixture mongo)
        : TrueMainWebApplicationFactory<Program>(
            postgres,
            [
                new KeyValuePair<string, string?>("MongoLogging:ConnectionString", mongo.ConnectionString),
                new KeyValuePair<string, string?>("MongoLogging:Database", MongoFixture.DatabaseName),
                new KeyValuePair<string, string?>("MongoLogging:LogsCollection", MongoFixture.LogsCollection),
                new KeyValuePair<string, string?>("MongoLogging:AuditCollection", MongoFixture.AuditCollection),
                new KeyValuePair<string, string?>("MongoLogging:CrashesCollection", MongoFixture.CrashesCollection),
                new KeyValuePair<string, string?>("MongoLogging:MinimumLevel", "None"),
                new KeyValuePair<string, string?>(
                    "MongoLogging:CrashFilePath",
                    Path.Combine(Path.GetTempPath(), $"truemain-crash-tests-{Guid.NewGuid():N}"))
            ]);

    private sealed class CrashesTestContract
    {
        public IReadOnlyList<CrashEntryTestContract> Entries { get; init; } = [];

        public long Total { get; init; }

        public int Page { get; init; }

        public int PageSize { get; init; }

        public IReadOnlyList<string> Sources { get; init; } = [];

        public IReadOnlyList<string> Processes { get; init; } = [];
    }

    private sealed class CrashEntryTestContract
    {
        public string Id { get; init; } = string.Empty;

        public DateTime TimestampUtc { get; init; }

        public string ProcessName { get; init; } = string.Empty;

        public string Source { get; init; } = string.Empty;

        public string Explanation { get; init; } = string.Empty;

        public string? ExceptionType { get; init; }

        public string? Message { get; init; }

        public string? StackTrace { get; init; }

        public IReadOnlyList<CrashExceptionTestContract> InnerExceptions { get; init; } = [];

        public string? Host { get; init; }

        public string? OsDescription { get; init; }

        public double UptimeSeconds { get; init; }

        public string? RuntimeVersion { get; init; }

        public string? AppVersion { get; init; }

        public long WorkingSetBytes { get; init; }

        public long TotalManagedMemoryBytes { get; init; }

        public int Gen0Collections { get; init; }

        public int Gen1Collections { get; init; }

        public int Gen2Collections { get; init; }

        public int? ExitCode { get; init; }

        public IReadOnlyList<CrashLogTailTestContract> RecentLogTail { get; init; } = [];
    }

    private sealed class CrashExceptionTestContract
    {
        public string Type { get; init; } = string.Empty;

        public string Message { get; init; } = string.Empty;

        public string? StackTrace { get; init; }
    }

    private sealed class CrashLogTailTestContract
    {
        public DateTime TimestampUtc { get; init; }

        public string Level { get; init; } = string.Empty;

        public string Category { get; init; } = string.Empty;

        public string Message { get; init; } = string.Empty;

        public string? Exception { get; init; }
    }
}
