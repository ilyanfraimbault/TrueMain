using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Ops;
using TrueMain.TestKit;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The queue-latency snapshot (#1024). Percentiles are computed in Postgres, so these
/// facts run against the real database rather than a fake: the point is that
/// <c>percentile_cont</c> and the per-leg <c>FILTER</c> clauses actually keep each leg
/// to the rows that carry both of its ends.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class CandidateQueueLatencyApiIntegrationTests
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;

    private readonly PostgresFixture _fixture;

    public CandidateQueueLatencyApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetQueueLatency_MeasuresEachLegOverTheRowsThatCarryBothOfItsEnds()
    {
        await _fixture.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        await using (var db = _fixture.CreateDbContext())
        {
            db.MainCandidates.AddRange(
                // Scored after 1h, validated 2h later.
                Candidate("puuid-a", 1, now.AddHours(-10), now.AddHours(-9), now.AddHours(-7)),
                // Scored after 3h, validated 4h later.
                Candidate("puuid-b", 2, now.AddHours(-10), now.AddHours(-7), now.AddHours(-3)),
                // Scored after 5h, never validated: first leg only.
                Candidate("puuid-c", 3, now.AddHours(-10), now.AddHours(-5), null),
                // Never scored: in neither leg, but still a retained candidate.
                Candidate("puuid-d", 4, now.AddHours(-10), null, null));

            await db.SaveChangesAsync();
        }

        await using var factory = new TrueMainWebApplicationFactory<Program>(_fixture);
        using var client = CreateClient(factory);

        var payload = await client.GetFromJsonAsync<CandidateQueueLatencyReadModel>(
            "/ops/candidates/queue-latency");

        payload.Should().NotBeNull();
        payload!.RetainedCandidates.Should().Be(4);

        payload.DiscoveredToScored.Samples.Should().Be(3, "one candidate was never scored");
        payload.DiscoveredToScored.MedianSeconds.Should().BeApproximately(
            TimeSpan.FromHours(3).TotalSeconds, 60);
        payload.DiscoveredToScored.P90Seconds.Should().BeApproximately(
            TimeSpan.FromHours(4.6).TotalSeconds, 60);

        payload.ScoredToValidated.Samples.Should().Be(2, "only two candidates carry both ends");
        payload.ScoredToValidated.MedianSeconds.Should().BeApproximately(
            TimeSpan.FromHours(3).TotalSeconds, 60);

        payload.AsOfUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task GetQueueLatency_ReportsNoSamplesRatherThanZeroLatency_WhenNothingHasMoved()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new TrueMainWebApplicationFactory<Program>(_fixture);
        using var client = CreateClient(factory);

        var payload = await client.GetFromJsonAsync<CandidateQueueLatencyReadModel>(
            "/ops/candidates/queue-latency");

        payload!.RetainedCandidates.Should().Be(0);
        payload.DiscoveredToScored.Samples.Should().Be(0);
        payload.DiscoveredToScored.MedianSeconds.Should().BeNull("no sample is not a latency of zero");
        payload.DiscoveredToScored.P90Seconds.Should().BeNull();
        payload.ScoredToValidated.MedianSeconds.Should().BeNull();
    }

    [Fact]
    public async Task GetQueueLatency_ShouldRequireOpsApiKey()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new TrueMainWebApplicationFactory<Program>(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/candidates/queue-latency");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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

    private static MainCandidate Candidate(
        string puuid,
        int championId,
        DateTime discoveredAtUtc,
        DateTime? scoredAtUtc,
        DateTime? validatedAtUtc)
        => new()
        {
            Id = Guid.NewGuid(),
            PlatformId = "EUW1",
            Puuid = puuid,
            ChampionId = championId,
            ChampionRankInMasteryTop = 1,
            ChampionPoints = 100_000,
            LastPlayTimeUtc = discoveredAtUtc,
            DiscoveredAtUtc = discoveredAtUtc,
            ScoredAtUtc = scoredAtUtc,
            ValidatedAtUtc = validatedAtUtc,
            Status = validatedAtUtc is null ? MainCandidateStatus.Scored : MainCandidateStatus.Validated
        };
}
