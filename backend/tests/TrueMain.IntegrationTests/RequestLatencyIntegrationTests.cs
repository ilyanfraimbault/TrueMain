using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Logging.Mongo;
using Data.Metrics.Mongo;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using TrueMain.ReadModels.Ops;
using TrueMain.RequestLatency;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The ops request-latency rollups (#2307) against a real Mongo and the real Api pipeline: the
/// keyed-histogram <c>$inc</c>-upsert, the binned <c>$group</c>, and the middleware timing an
/// ops request end to end into <c>GET /ops/request-latency</c>.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RequestLatencyIntegrationTests(PostgresFixture fixture, MongoFixture mongo) : IAsyncLifetime
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private static readonly DateTime Minute = new(2026, 10, 6, 12, 34, 0, DateTimeKind.Utc);

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetDatabaseAsync();
        await mongo.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task WriteAsync_TwoFlushesOfOneMinute_AddUpIntoOneDocument()
    {
        using var context = BuildContext();
        var store = BuildStore(context);

        await store.WriteAsync([Rollup(Minute, "/ops/logs", [40, 40, 900])], CancellationToken.None);
        await store.WriteAsync([Rollup(Minute, "/ops/logs", [2_000])], CancellationToken.None);

        var documents = await mongo.GetCollection<RequestLatencyRollupDocument>(MongoFixture.RequestLatencyCollection)
            .Find(FilterDefinition<RequestLatencyRollupDocument>.Empty).ToListAsync();
        var document = documents.Should().ContainSingle().Subject;
        document.Count.Should().Be(4);
        document.MaxMs.Should().Be(2_000);
        document.SumMs.Should().Be(2_980);
        document.Histogram.Should().Contain(RequestLatencyRollupDocument.BucketKey(RequestLatencyHistogram.BucketOf(40)), 2);
        document.Histogram.Should().Contain(RequestLatencyRollupDocument.BucketKey(RequestLatencyHistogram.BucketOf(2_000)), 1);
        document.ProcessName.Should().Be("Api");
    }

    [Fact]
    public async Task GetWindowAsync_SumsTheMinutesOfEachBin_AndReportsTheOldestMinute()
    {
        using var context = BuildContext();
        var store = BuildStore(context);

        await store.WriteAsync(
            [
                Rollup(Minute.AddHours(-3), "/ops/logs", [10]),
                Rollup(Minute, "/ops/logs", [10, 20]),
                Rollup(Minute.AddMinutes(5), "/ops/logs", [3_000]),
                Rollup(Minute.AddHours(1), "/ops/logs", [30]),
                Rollup(Minute, "/ops/overview", [400])
            ],
            CancellationToken.None);

        var window = await store.GetWindowAsync(Minute.AddHours(-1), binHours: 1, CancellationToken.None);

        window.OldestRetainedBucketUtc.Should().Be(Minute.AddHours(-3));
        var logs = window.Bins.Where(bin => bin.Route == "/ops/logs").OrderBy(bin => bin.BinStartUtc).ToList();
        logs.Should().HaveCount(2, "the minute before the window is not read");
        logs[0].BinStartUtc.Should().Be(new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc));
        logs[0].Count.Should().Be(3);
        logs[0].MaxMs.Should().Be(3_000);
        logs[0].Histogram.Sum().Should().Be(3);
        logs[1].Count.Should().Be(1);
        window.Bins.Should().ContainSingle(bin => bin.Route == "/ops/overview");
    }

    [Fact]
    public async Task OpsRequests_AreTimedPerRouteTemplate_AndServedSlowestFirst()
    {
        await using var factory = new ApiWebApplicationFactory(fixture, mongo);
        using var client = CreateClient(factory);

        // Two real ops requests, then a seeded slow route: the middleware times the first, the
        // store sorts them against the second.
        (await client.GetAsync("/ops/process-runs")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/ops/process-runs?page=2")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/healthz")).StatusCode.Should().Be(HttpStatusCode.OK);

        var store = factory.Services.GetRequiredService<IRequestLatencyStore>();
        var accumulator = factory.Services.GetRequiredService<RequestLatencyAccumulator>();
        var recent = DateTime.UtcNow.AddMinutes(-2);
        await store.WriteAsync(accumulator.Drain(), CancellationToken.None);
        await store.WriteAsync(
            [Rollup(new DateTime(recent.Year, recent.Month, recent.Day, recent.Hour, recent.Minute, 0, DateTimeKind.Utc), "/ops/stats/overview", [6_000, 7_000])],
            CancellationToken.None);

        var payload = await client.GetFromJsonAsync<RequestLatencyReadModel>("/ops/request-latency?window=7d");

        payload.Should().NotBeNull();
        payload!.Window.Should().Be("7d");
        payload.BinHours.Should().Be(6);
        payload.SlowThresholdMs.Should().Be(1_000);
        payload.Routes.Select(route => route.Route).Should().ContainInOrder("/ops/stats/overview", "/ops/process-runs");
        payload.Routes.Should().NotContain(route => route.Route == "/healthz", "only the ops routes are timed");
        var processRuns = payload.Routes.Single(route => route.Route == "/ops/process-runs");
        processRuns.Method.Should().Be("GET");
        processRuns.Count.Should().Be(2, "both requests share one route template");
        processRuns.ServerErrors.Should().Be(0);
        processRuns.Series.Should().HaveCount(payload.Routes[0].Series.Count);
        processRuns.Series.Sum(point => point.Count).Should().Be(2);
        var slow = payload.Routes[0];
        slow.P95Ms.Should().BeGreaterThan(5_000).And.BeLessThanOrEqualTo(7_000);
        slow.MaxMs.Should().Be(7_000);
    }

    [Fact]
    public async Task GetRequestLatency_WithNothingRecorded_HasNoOldestMinute()
    {
        await using var factory = new ApiWebApplicationFactory(fixture, mongo);
        using var client = CreateClient(factory);

        var payload = await client.GetFromJsonAsync<RequestLatencyReadModel>("/ops/request-latency");

        payload!.Window.Should().Be("24h");
        payload.BinHours.Should().Be(1);
        payload.Routes.Should().BeEmpty();
        payload.OldestRetainedUtc.Should().BeNull("nothing recorded is not a quiet window");
        payload.RetentionDays.Should().Be(30);
    }

    [Fact]
    public async Task GetRequestLatency_ShouldRequireOpsApiKey()
    {
        await using var factory = new ApiWebApplicationFactory(fixture, mongo);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/request-latency");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static RequestLatencyRollup Rollup(DateTime minute, string route, IReadOnlyList<double> durationsMs)
    {
        var histogram = new long[RequestLatencyHistogram.BucketCount];
        foreach (var duration in durationsMs)
        {
            histogram[RequestLatencyHistogram.BucketOf(duration)]++;
        }

        return new RequestLatencyRollup(
            minute,
            route,
            "GET",
            durationsMs.Count,
            durationsMs.Sum(),
            durationsMs.Max(),
            histogram,
            Timeouts: 0,
            ServerErrors: 0,
            minute.AddSeconds(30));
    }

    private static HttpClient CreateClient(TrueMainWebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }

    private RequestLatencyStore BuildStore(MongoLogContext context)
        => new(context, Microsoft.Extensions.Options.Options.Create(Settings(mongo.ConnectionString)));

    private MongoLogContext BuildContext() => new(Microsoft.Extensions.Options.Options.Create(Settings(mongo.ConnectionString)));

    private static MongoLoggingOptions Settings(string connectionString) => new()
    {
        ConnectionString = connectionString,
        Database = MongoFixture.DatabaseName,
        ProcessName = "Api",
        Enabled = true
    };

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture, MongoFixture mongo)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MongoLogging:ConnectionString", mongo.ConnectionString),
                new KeyValuePair<string, string?>("MongoLogging:Database", MongoFixture.DatabaseName),
                new KeyValuePair<string, string?>("MongoLogging:MinimumLevel", "None")
            ]);
}
