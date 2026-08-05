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
/// End-to-end candidate funnel (#1024): seeds recorded runs of the six contributing
/// processes in Mongo and asserts the endpoint splits their summaries into the right
/// series. Postgres is needed only because the API host requires it — the series never
/// reads <c>main_candidates</c>, which is the whole point: pruning a candidate must not
/// rewrite the bucket that discovered it.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class CandidateFunnelApiIntegrationTests
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;

    private readonly PostgresFixture _fixture;
    private readonly MongoFixture _mongo;

    public CandidateFunnelApiIntegrationTests(PostgresFixture fixture, MongoFixture mongo)
    {
        _fixture = fixture;
        _mongo = mongo;
    }

    [Fact]
    public async Task GetCandidateFunnel_SplitsEachProcessIntoItsOwnSeries()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        var today = DateTime.UtcNow.Date;
        await Runs().InsertManyAsync(
        [
            Run("Discovery", today.AddHours(1), new
            {
                platforms = new[]
                {
                    new { platform = "EUW1", accountsProcessed = 40, newAccounts = 3, candidatesInserted = 30, candidatesUpdated = 5, rankSnapshotsInserted = 0, rankSnapshotsUpdated = 0, rankSnapshotsUnchanged = 0, error = (string?)null },
                    new { platform = "KR", accountsProcessed = 20, newAccounts = 1, candidatesInserted = 12, candidatesUpdated = 2, rankSnapshotsInserted = 0, rankSnapshotsUpdated = 0, rankSnapshotsUnchanged = 0, error = (string?)null },
                }
            }),
            Run("Harvest", today.AddHours(2), new
            {
                candidatesInserted = 7,
                candidatesUpdated = 1,
                accountsCreated = 0,
                eligibleNew = 0,
                selectedNew = 0,
                eligibleKnown = 0,
                selectedKnown = 0,
                budgetExhausted = false
            }),
            Run("ManualSeed", today.AddHours(3), new
            {
                claimed = 2,
                ingested = 2,
                notFound = 0,
                failed = 0,
                candidatesQueued = 2
            }),
            Run("Scoring", today.AddHours(4), new
            {
                platforms = new[]
                {
                    new { platform = "EUW1", scored = 100, queued = 20 },
                    new { platform = "KR", scored = 40, queued = 10 },
                }
            }),
            Run("MatchIngestion", today.AddHours(5), new
            {
                accountsProcessed = 30,
                matchesInserted = 300,
                matchesSkipped = 4,
                timelinesUpdated = 12,
                errors = 0,
                accountsValidated = 25,
                byPlatform = Array.Empty<object>()
            }),
            Run("MainAnalysis", today.AddHours(6), new
            {
                accountsProcessed = 50,
                statsUpserted = 40,
                statsRemoved = 2,
                demotedAccounts = 3
            }),
            // A process that touches candidates but does not advance them: retention
            // deletes stale rows, which is not a funnel stage and must not be counted.
            Run("MatchDataRetention", today.AddHours(7), new { prunedCandidates = 999 }),
        ]);

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/ops/candidates/funnel?granularity=day&windowDays=7");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<CandidateFunnelReadModel>();
        payload.Should().NotBeNull();

        var bucket = payload!.Buckets.Single(b => b.Bucket == today.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"));
        bucket.IntakeLadder.Should().Be(42);
        bucket.IntakeHarvest.Should().Be(7);
        bucket.IntakeManual.Should().Be(2);
        bucket.Scored.Should().Be(140);
        bucket.Promoted.Should().Be(30);
        bucket.Validated.Should().Be(25);
        bucket.Demoted.Should().Be(3);
        bucket.Runs.Should().Be(6, "retention's run is not a funnel stage");

        payload.WindowDays.Should().Be(7);
        payload.RetentionDays.Should().BePositive("the panel states the bound it cannot see past");
    }

    [Fact]
    public async Task GetCandidateFunnel_ReportsValidatedAsAbsentForRunsRecordedBeforeTheCounter()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        var today = DateTime.UtcNow.Date;
        await Runs().InsertManyAsync(
        [
            // The pre-#1024 shape: no accountsValidated key at all.
            Run("MatchIngestion", today.AddDays(-2).AddHours(4), new
            {
                accountsProcessed = 9,
                matchesInserted = 30,
                matchesSkipped = 0,
                timelinesUpdated = 0,
                errors = 0,
                byPlatform = Array.Empty<object>()
            }),
        ]);

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);

        var payload = await client.GetFromJsonAsync<CandidateFunnelReadModel>(
            "/ops/candidates/funnel?granularity=day&windowDays=7");

        payload!.ValidatedFirstMeasuredAtUtc.Should().BeNull();
        payload.Buckets.Should().NotBeEmpty();
        payload.Buckets.Should().OnlyContain(
            b => b.Validated == null,
            "a health panel may not pass off what it did not measure as a measured zero");
    }

    [Fact]
    public async Task GetCandidateFunnel_ReturnsNoBucketsWhenNoRunSurvives()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);

        var payload = await client.GetFromJsonAsync<CandidateFunnelReadModel>(
            "/ops/candidates/funnel?granularity=day");

        payload!.Buckets.Should().BeEmpty();
        payload.EarliestRunAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData("/ops/candidates/funnel")]
    [InlineData("/ops/candidates/funnel?granularity=")]
    [InlineData("/ops/candidates/funnel?granularity=decade")]
    [InlineData("/ops/candidates/funnel?granularity=patch")]
    [InlineData("/ops/candidates/funnel?granularity=year")]
    public async Task GetCandidateFunnel_InvalidGranularity_ShouldReturn400ProblemDetails(string url)
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
    public async Task GetCandidateFunnel_ShouldRequireOpsApiKey()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/candidates/funnel?granularity=day");
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

    // Summaries are handed in as anonymous objects shaped exactly like the records
    // serialize — camelCase, as locked by ProcessRunSummaryJsonTests.
    private static ProcessRunDocument Run(string processName, DateTime startedAtUtc, object summary)
        => new()
        {
            Id = Guid.NewGuid(),
            ProcessName = processName,
            StartedAtUtc = startedAtUtc,
            FinishedAtUtc = startedAtUtc.AddMinutes(2),
            DurationMs = (int)TimeSpan.FromMinutes(2).TotalMilliseconds,
            Status = ProcessRunStatus.Success,
            Host = "test-host",
            SummaryJson = JsonSerializer.Serialize(summary)
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
