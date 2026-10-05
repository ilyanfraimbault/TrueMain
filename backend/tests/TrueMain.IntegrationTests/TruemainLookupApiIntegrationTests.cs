using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Core.Lol.Ranking;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.IntegrationTests;

/// <summary>
/// <c>GET /truemains/lookup</c> (#1910): the desktop app's true-main mark. It
/// must admit exactly the leaderboard's population, matched by Riot ID on the
/// game's platform.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class TruemainLookupApiIntegrationTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public TruemainLookupApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Finds_a_true_main_of_the_champion_and_leaves_everyone_else_out()
    {
        await using (var db = _fixture.CreateDbContext())
        {
            var ahri = Ranked(Account("ahri", "AhriMain", "EUW1", "EUW"));
            var other = Ranked(Account("other", "ZedMain", "EUW1", "EUW"));
            var casual = Ranked(Account("casual", "Casual", "EUW1", "EUW"));
            db.RiotAccounts.AddRange(ahri, other, casual);
            db.MainChampionStats.AddRange(
                MainStat("ahri", "EUW1", 103, isOtp: true, masteryPoints: 900_000),
                MainStat("other", "EUW1", 238),
                MainStat("casual", "EUW1", 103, isMain: false));
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // ZedMain is on Ahri this game: a true main of another champion gets no mark.
        var response = await client.GetFromJsonAsync<TruemainLookupResponse>(
            Url("EUW1", "AhriMain#EUW:103", "ZedMain#EUW:103", "Casual#EUW:103", "Unknown#EUW:103"));

        var entry = response!.Players.Should().ContainSingle().Subject;
        entry.RiotId.Should().Be("AhriMain#EUW");
        entry.NameTag.Should().Be("AhriMain-EUW");
        entry.ChampionId.Should().Be(103);
        entry.ChampionMatches.Should().Be(38);
        entry.TotalMatches.Should().Be(50);
        entry.IsOtp.Should().BeTrue();
        entry.MasteryPoints.Should().Be(900_000);
        entry.Dedication.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Matches_the_riot_id_case_insensitively()
    {
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(Ranked(Account("ahri", "AhriMain", "EUW1", "EUW")));
            db.MainChampionStats.Add(MainStat("ahri", "EUW1", 103));
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetFromJsonAsync<TruemainLookupResponse>(Url("euw1", "aHRImain#euw:103"));

        response!.Players.Should().ContainSingle().Which.RiotId.Should().Be("AhriMain#EUW");
    }

    [Fact]
    public async Task A_riot_id_on_another_platform_is_not_matched()
    {
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(Ranked(Account("kr", "Faker", "KR", "KR1")));
            db.MainChampionStats.Add(MainStat("kr", "KR", 7));
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var elsewhere = await client.GetFromJsonAsync<TruemainLookupResponse>(Url("EUW1", "Faker#KR1:7"));
        elsewhere!.Players.Should().BeEmpty();

        var home = await client.GetFromJsonAsync<TruemainLookupResponse>(Url("KR", "Faker#KR1:7"));
        home!.Players.Should().ContainSingle();
    }

    [Fact]
    public async Task An_inactive_or_unranked_main_is_not_marked()
    {
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.AddRange(
                Ranked(Account("inactive", "Retired", "EUW1", "EUW")),
                Account("unranked", "Unranked", "EUW1", "EUW"));
            db.MainChampionStats.AddRange(
                MainStat("inactive", "EUW1", 103, isActive: false),
                MainStat("unranked", "EUW1", 103));
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetFromJsonAsync<TruemainLookupResponse>(
            Url("EUW1", "Retired#EUW:103", "Unranked#EUW:103"));

        response!.Players.Should().BeEmpty();
    }

    [Fact]
    public async Task The_most_recently_active_holder_of_a_riot_id_decides()
    {
        var now = DateTime.UtcNow;
        await using (var db = _fixture.CreateDbContext())
        {
            // A recycled Riot ID: the stale row was a true main, the current holder is not.
            var stale = Ranked(Account("stale", "Recycled", "EUW1", "EUW"));
            stale.LastMatchIngestAtUtc = now.AddDays(-200);
            var current = Ranked(Account("current", "Recycled", "EUW1", "EUW"));
            current.LastMatchIngestAtUtc = now;
            db.RiotAccounts.AddRange(stale, current);
            db.MainChampionStats.Add(MainStat("stale", "EUW1", 103));
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetFromJsonAsync<TruemainLookupResponse>(Url("EUW1", "Recycled#EUW:103"));

        response!.Players.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/truemains/lookup?player=A%23EUW:1")]
    [InlineData("/truemains/lookup?platformId=EUW1")]
    [InlineData("/truemains/lookup?platformId=EUW1&player=NoTag:1")]
    [InlineData("/truemains/lookup?platformId=EUW1&player=A%23EUW:0")]
    public async Task Rejects_a_malformed_request(string url)
    {
        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Rejects_more_than_ten_players()
    {
        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var players = Enumerable.Range(1, 11).Select(i => $"P{i}#EUW:{i}").ToArray();
        var response = await client.GetAsync(Url("EUW1", players));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static string Url(string platformId, params string[] players)
        => $"/truemains/lookup?platformId={platformId}"
           + string.Concat(players.Select(player => $"&player={Uri.EscapeDataString(player)}"));

    private static RiotAccount Account(string puuid, string gameName, string platformId, string tagLine)
        => new()
        {
            Id = Guid.NewGuid(),
            Puuid = puuid,
            GameName = gameName,
            TagLine = tagLine,
            PlatformId = platformId,
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            LastMatchIngestAtUtc = DateTime.UtcNow,
        };

    // The denormalised rank Score is the leaderboard's is-ranked gate.
    private static RiotAccount Ranked(RiotAccount account)
    {
        account.Score = RankScore.Compute("DIAMOND", "II", 50);
        return account;
    }

    private static MainChampionStat MainStat(
        string puuid,
        string platformId,
        int championId,
        bool isMain = true,
        bool isActive = true,
        bool isOtp = false,
        long? masteryPoints = null)
        => new()
        {
            Id = Guid.NewGuid(),
            PlatformId = platformId,
            Puuid = puuid,
            ChampionId = championId,
            TotalMatches = 50,
            ChampionMatches = 38,
            PlayRate = 0.76d,
            IsMain = isMain,
            IsActive = isActive,
            IsOtp = isOtp,
            MasteryPoints = masteryPoints,
            MasteryRank = masteryPoints is null ? null : 1,
            PrimaryPosition = "MIDDLE",
            PositionBreakdown = [new PositionStat { Position = "MIDDLE", Games = 38, Rate = 1d }],
            CalculatedAtUtc = DateTime.UtcNow,
        };

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420"),
            ]);
}
