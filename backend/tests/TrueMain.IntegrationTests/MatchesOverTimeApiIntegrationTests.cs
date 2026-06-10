using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Core.Lol.Map;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class MatchesOverTimeApiIntegrationTests
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;

    public MatchesOverTimeApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetMatchesOverTime_Month_ShouldBucketByGameMonthInAscendingOrder()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMatchesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/ops/stats/matches-over-time?granularity=month");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        document.RootElement[0].EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["bucket", "matches"]);

        var buckets = await response.Content.ReadFromJsonAsync<IReadOnlyList<MatchTimeBucketTestContract>>();
        buckets.Should().NotBeNull();

        // Seeded distribution by game month (UTC):
        //   2026-03: 3 matches (2x EUW1 16.4 + 1x KR 16.4)
        //   2026-05: 2 matches (2x EUW1 16.10)
        buckets!.Should().HaveCount(2);

        // Month buckets are the ISO-8601 UTC timestamp of the truncated month start.
        buckets[0].Bucket.Should().Be("2026-03-01T00:00:00Z");
        buckets[0].Matches.Should().Be(3);
        buckets[1].Bucket.Should().Be("2026-05-01T00:00:00Z");
        buckets[1].Matches.Should().Be(2);

        // Explicitly assert chronological (ascending) ordering of the period starts.
        buckets.Select(b => b.Bucket).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetMatchesOverTime_Patch_ShouldBucketByNormalizedPatchOrderedChronologically()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMatchesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/ops/stats/matches-over-time?granularity=patch");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var buckets = await response.Content.ReadFromJsonAsync<IReadOnlyList<MatchTimeBucketTestContract>>();
        buckets.Should().NotBeNull();

        // Two normalized patches: "16.4" (March, 3 matches) and "16.10" (May, 2).
        buckets!.Should().HaveCount(2);

        // Ordering is chronological by earliest game per patch, NOT lexical: 16.4's
        // games predate 16.10's, so 16.4 must come first even though the string
        // "16.10" sorts before "16.4" lexically. This is the 16.10 < 16.4 trap.
        // A naive lexical ORDER BY on the patch string would have yielded
        // ["16.10", "16.4"]; the chronological ordering gives ["16.4", "16.10"].
        buckets.Select(b => b.Bucket).Should().Equal("16.4", "16.10");
        buckets[0].Matches.Should().Be(3);
        buckets[1].Matches.Should().Be(2);
    }

    [Fact]
    public async Task GetMatchesOverTime_Patch_WithRegion_ShouldFilterByPlatformId()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMatchesAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // region=KR isolates the single KR match (16.4, March).
        var response = await client.GetAsync("/ops/stats/matches-over-time?granularity=patch&region=KR");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var buckets = await response.Content.ReadFromJsonAsync<IReadOnlyList<MatchTimeBucketTestContract>>();
        buckets.Should().NotBeNull();

        buckets!.Should().ContainSingle();
        buckets[0].Bucket.Should().Be("16.4");
        buckets[0].Matches.Should().Be(1);
    }

    [Theory]
    [InlineData("/ops/stats/matches-over-time")]
    [InlineData("/ops/stats/matches-over-time?granularity=")]
    [InlineData("/ops/stats/matches-over-time?granularity=day")]
    [InlineData("/ops/stats/matches-over-time?granularity=quarter")]
    public async Task GetMatchesOverTime_InvalidGranularity_ShouldReturn400ProblemDetails(string url)
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task GetMatchesOverTime_ShouldRequireOpsApiKey()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/stats/matches-over-time?granularity=month");
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

    // Five matches spanning 2 months (2026-03, 2026-05) and 2 normalized patches
    // ("16.4", "16.10"). The patch with the lexically-larger string ("16.10") is the
    // chronologically-later one, which is what makes the ordering assertions meaningful.
    private async Task SeedMatchesAsync()
    {
        await using var db = _fixture.CreateDbContext();

        db.Matches.AddRange(
            // March 2026, patch 16.4
            BuildMatch("MOT_1", "EUW1", "16.4.521.123", new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc)),
            BuildMatch("MOT_2", "EUW1", "16.4.521.123", new DateTime(2026, 3, 20, 8, 30, 0, DateTimeKind.Utc)),
            BuildMatch("MOT_3", "KR", "16.4.600.001", new DateTime(2026, 3, 25, 23, 0, 0, DateTimeKind.Utc)),
            // May 2026, patch 16.10
            BuildMatch("MOT_4", "EUW1", "16.10.700.001", new DateTime(2026, 5, 5, 6, 0, 0, DateTimeKind.Utc)),
            BuildMatch("MOT_5", "EUW1", "16.10.700.002", new DateTime(2026, 5, 15, 18, 45, 0, DateTimeKind.Utc)));

        await db.SaveChangesAsync();
    }

    private static Match BuildMatch(string id, string platformId, string gameVersion, DateTime startUtc)
        => new()
        {
            Id = id,
            PlatformId = platformId,
            QueueId = (int)LolQueueId.RankedSoloDuo,
            MapId = (int)LolMapId.SummonersRift,
            GameMode = "CLASSIC",
            GameType = "MATCHED_GAME",
            GameStartTimeUtc = startUtc,
            GameDurationSeconds = 1800,
            GameVersion = gameVersion,
            CreatedAtUtc = startUtc,
            TimelineIngested = true
        };

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(fixture);

    private sealed class MatchTimeBucketTestContract
    {
        public string Bucket { get; init; } = string.Empty;

        public long Matches { get; init; }
    }
}
