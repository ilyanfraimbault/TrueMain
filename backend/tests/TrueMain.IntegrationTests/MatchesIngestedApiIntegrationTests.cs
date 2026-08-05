using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Data.Entities;
using Data.Ops.Mongo;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Driver;
using TrueMain.ReadModels.Ops;
using TrueMain.TestKit;

namespace TrueMain.IntegrationTests;

/// <summary>
/// End-to-end ingestion-throughput series (#1025): seeds recorded
/// <c>MatchIngestion</c> runs in Mongo and asserts the endpoint buckets their
/// summaries. Postgres is needed only because the API host requires it — the series
/// itself never touches it, which is the whole point: deleting a match must not
/// rewrite the run that ingested it.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MatchesIngestedApiIntegrationTests
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;

    private readonly PostgresFixture _fixture;
    private readonly MongoFixture _mongo;

    public MatchesIngestedApiIntegrationTests(PostgresFixture fixture, MongoFixture mongo)
    {
        _fixture = fixture;
        _mongo = mongo;
    }

    [Fact]
    public async Task GetMatchesIngested_SumsRunSummariesPerDayAndReportsTheRetentionBound()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        var today = DateTime.UtcNow.Date;
        await Runs().InsertManyAsync(
        [
            Run("MatchIngestion", today.AddHours(2), inserted: 30, skipped: 4, timelines: 12),
            Run("MatchIngestion", today.AddHours(6), inserted: 12, skipped: 1, timelines: 6),
            // Another process's run in the same day must not leak into the series.
            Run("Discovery", today.AddHours(3), inserted: 999, skipped: 999, timelines: 999),
        ]);

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/ops/stats/matches-ingested?granularity=day&windowDays=7");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<MatchesIngestedReadModel>();
        payload.Should().NotBeNull();

        var bucket = payload!.Buckets.Single(b => b.Bucket == today.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
        bucket.MatchesInserted.Should().Be(42, "Discovery's run must not be counted");
        bucket.MatchesSkipped.Should().Be(5);
        bucket.TimelinesUpdated.Should().Be(18);
        bucket.Runs.Should().Be(2);

        payload.WindowDays.Should().Be(7);
        payload.RetentionDays.Should().BePositive("the panel states the bound it cannot see past");
    }

    [Fact]
    public async Task GetMatchesIngested_ReturnsNoBucketsWhenNoRunSurvives()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);

        var payload = await client.GetFromJsonAsync<MatchesIngestedReadModel>(
            "/ops/stats/matches-ingested?granularity=day");

        // An unmeasured range is empty, never a run of zeros: zeros would assert an
        // idle pipeline over days retention has already dropped.
        payload!.Buckets.Should().BeEmpty();
        payload.EarliestRunAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData("/ops/stats/matches-ingested")]
    [InlineData("/ops/stats/matches-ingested?granularity=")]
    [InlineData("/ops/stats/matches-ingested?granularity=decade")]
    // Rejected on purpose although MatchTimeGranularity accepts them: a patch is a
    // property of the games, not of when we ingested them, and a year cannot fill two
    // buckets under the run retention.
    [InlineData("/ops/stats/matches-ingested?granularity=patch")]
    [InlineData("/ops/stats/matches-ingested?granularity=year")]
    public async Task GetMatchesIngested_InvalidGranularity_ShouldReturn400ProblemDetails(string url)
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);

        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task GetMatchesIngested_ShouldRequireOpsApiKey()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/stats/matches-ingested?granularity=day");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }

    private IMongoCollection<ProcessRunDocument> Runs()
        => _mongo.GetCollection<ProcessRunDocument>(MongoFixture.ProcessRunsCollection);

    // Summary shaped exactly like MatchIngestionSummary serializes — camelCase, as
    // locked by ProcessRunSummaryJsonTests.
    private static ProcessRunDocument Run(
        string processName,
        DateTime startedAtUtc,
        int inserted,
        int skipped,
        int timelines)
        => new()
        {
            Id = Guid.NewGuid(),
            ProcessName = processName,
            StartedAtUtc = startedAtUtc,
            FinishedAtUtc = startedAtUtc.AddMinutes(2),
            DurationMs = (int)TimeSpan.FromMinutes(2).TotalMilliseconds,
            Status = ProcessRunStatus.Success,
            Host = "test-host",
            SummaryJson = JsonSerializer.Serialize(new
            {
                accountsProcessed = 9,
                matchesInserted = inserted,
                matchesSkipped = skipped,
                timelinesUpdated = timelines,
                errors = 0,
                byPlatform = Array.Empty<object>()
            })
        };

    // Point the host at the test Mongo container (the series is read from the Mongo
    // process-run store, not from Postgres) and mute the diagnostic sink so incidental
    // host warnings never write extra documents.
    private sealed class ApiWebApplicationFactory(PostgresFixture fixture, MongoFixture mongo)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MongoLogging:ConnectionString", mongo.ConnectionString),
                new KeyValuePair<string, string?>("MongoLogging:Database", MongoFixture.DatabaseName),
                new KeyValuePair<string, string?>("MongoLogging:MinimumLevel", "None")
            ]);
}
