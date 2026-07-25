using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Core.Lol.Ranking;
using Core.Truemains;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.IntegrationTests;

/// <summary>
/// End-to-end cover for the dedication score (#530). The scoring maths itself is
/// unit-tested (<c>DedicationScoreTests</c>); what needs a real Postgres is the
/// query behind it — the <c>DISTINCT ON</c> that picks the signature champion and
/// the <c>LEFT JOIN LATERAL</c> that measures its career over
/// <c>champion_aggregate_scopes</c> — plus the in-memory ranking the
/// <c>?sort=dedication</c> path pages on.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class TruemainsDedicationApiIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public TruemainsDedicationApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Profile_scores_the_signature_champion_from_its_aggregate_history()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        var account = Account("devoted-puuid", "Devoted", "EUW1");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);
            db.RankSnapshots.Add(Snapshot(account, "DIAMOND", "I", 40, now));

            // Signature champion: Yasuo at 0.7 play rate. Ahri is also a main but
            // sits lower, so the score must be about Yasuo.
            db.MainChampionStats.Add(MainStat(account, championId: 157, playRate: 0.7d, now));
            db.MainChampionStats.Add(MainStat(account, championId: 103, playRate: 0.2d, now));

            // Yasuo career: 3 patches, 60 games, last played 2 days ago. The
            // three scope rows must be summed, and the patches counted distinct.
            db.ChampionAggregateScopes.AddRange(
                Scope(account.Id, 157, "15.1.1", games: 20, now.AddDays(-30)),
                Scope(account.Id, 157, "15.2.1", games: 25, now.AddDays(-10)),
                Scope(account.Id, 157, "15.3.1", games: 15, now.AddDays(-2)));
            // Ahri career — must not leak into Yasuo's totals.
            db.ChampionAggregateScopes.Add(Scope(account.Id, 103, "15.3.1", games: 40, now.AddDays(-1)));
            // A different queue on the signature champion: out of scope.
            var otherQueue = Scope(account.Id, 157, "15.3.1", games: 500, now);
            otherQueue.QueueId = 400;
            db.ChampionAggregateScopes.Add(otherQueue);

            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var profile = await client.GetFromJsonAsync<ProfileReadModel>("/truemains/Devoted-EUW1/profile");

        profile.Should().NotBeNull();
        profile!.Dedication.Should().NotBeNull();
        var dedication = profile.Dedication!;

        dedication.ChampionId.Should().Be(157, "Yasuo is the top main by play rate");
        dedication.PlayRate.Should().BeApproximately(0.7d, 1e-9);
        dedication.CareerGames.Should().Be(60, "the three ranked-solo Yasuo scopes sum to 60");
        dedication.PatchSpan.Should().Be(3, "three distinct game versions carry Yasuo games");
        dedication.DaysSinceLastGame.Should().Be(2);

        // The endpoint must return exactly what the pure function produces for
        // those inputs — the read model is a projection, not a second formula.
        var expected = DedicationScore.Compute(new DedicationInputs(
            PlayRate: 0.7d,
            CareerGames: 60,
            PatchSpan: 3,
            DaysSinceLastGame: dedication.DaysSinceLastGame!.Value));

        dedication.Commitment.Should().BeApproximately(expected.Commitment, 1e-9);
        dedication.Span.Should().BeApproximately(expected.Span, 1e-9);
        dedication.Volume.Should().BeApproximately(expected.Volume, 1e-9);
        // Recency is derived from a live clock, so the day count is pinned above
        // and the component only has to land in the same neighbourhood.
        dedication.Recency.Should().BeApproximately(expected.Recency, 0.01d);
        dedication.Score.Should().BeApproximately(expected.Score, 1d);
    }

    [Fact]
    public async Task Profile_scores_a_main_with_no_aggregates_on_commitment_alone()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        var account = Account("fresh-puuid", "Fresh", "EUW1");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);
            db.RankSnapshots.Add(Snapshot(account, "DIAMOND", "IV", 10, now));
            db.MainChampionStats.Add(MainStat(account, championId: 64, playRate: 1d, now));
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var profile = await client.GetFromJsonAsync<ProfileReadModel>("/truemains/Fresh-EUW1/profile");

        profile!.Dedication.Should().NotBeNull();
        // The LEFT JOIN LATERAL yields NULLs, which must coalesce to a scoreable
        // zero rather than dropping the row or throwing.
        profile.Dedication!.CareerGames.Should().Be(0);
        profile.Dedication.PatchSpan.Should().Be(0);
        profile.Dedication.DaysSinceLastGame.Should().BeNull();
        profile.Dedication.Recency.Should().Be(0d);
        profile.Dedication.Score.Should().BeApproximately(100d * DedicationScore.CommitmentWeight, 0.05d);
    }

    [Fact]
    public async Task Leaderboard_sorted_by_dedication_reorders_the_rank_ladder()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // Two accounts whose rank order is the inverse of their dedication:
        // the higher-LP player dabbles, the lower-LP one is a career one-trick.
        var apexDabbler = Account("apex-puuid", "ApexDabbler", "EUW1");
        var lowerOneTrick = Account("otp-puuid", "LowerOneTrick", "EUW1");

        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.AddRange(apexDabbler, lowerOneTrick);
            db.RankSnapshots.AddRange(
                Snapshot(apexDabbler, "CHALLENGER", "I", 900, now),
                Snapshot(lowerOneTrick, "DIAMOND", "IV", 0, now));

            db.MainChampionStats.AddRange(
                MainStat(apexDabbler, championId: 103, playRate: 0.22d, now),
                MainStat(lowerOneTrick, championId: 157, playRate: 0.95d, now));

            db.ChampionAggregateScopes.AddRange(
                Scope(apexDabbler.Id, 103, "15.3.1", games: 12, now.AddDays(-20)),
                Scope(lowerOneTrick.Id, 157, "15.1.1", games: 90, now.AddDays(-25)),
                Scope(lowerOneTrick.Id, 157, "15.2.1", games: 90, now.AddDays(-12)),
                Scope(lowerOneTrick.Id, 157, "15.3.1", games: 90, now));

            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var byRank = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains");
        byRank!.Rows.Select(r => r.Identity.GameName)
            .Should().ContainInOrder("ApexDabbler", "LowerOneTrick");

        var byDedication = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains?sort=dedication");
        byDedication!.Total.Should().Be(2);
        byDedication.Rows.Select(r => r.Identity.GameName)
            .Should().ContainInOrder("LowerOneTrick", "ApexDabbler");

        // Ranks are recomputed for the active order, not carried over.
        byDedication.Rows[0].Rank.Should().Be(1);
        byDedication.Rows[1].Rank.Should().Be(2);

        // Both orderings carry the same score for the same player.
        var otpByRank = byRank.Rows.Single(r => r.Identity.GameName == "LowerOneTrick").Dedication;
        var otpByDedication = byDedication.Rows.Single(r => r.Identity.GameName == "LowerOneTrick").Dedication;
        otpByRank.Should().NotBeNull();
        otpByDedication.Should().NotBeNull();
        otpByDedication!.Score.Should().Be(otpByRank!.Score);
        otpByDedication.CareerGames.Should().Be(270);
        otpByDedication.PatchSpan.Should().Be(3);
    }

    [Fact]
    public async Task Leaderboard_scores_the_filtered_champion_not_the_top_main()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        var account = Account("flex-puuid", "Flex", "EUW1");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(account);
            db.RankSnapshots.Add(Snapshot(account, "DIAMOND", "I", 40, now));
            db.MainChampionStats.AddRange(
                MainStat(account, championId: 157, playRate: 0.6d, now),
                MainStat(account, championId: 103, playRate: 0.3d, now));
            db.ChampionAggregateScopes.AddRange(
                Scope(account.Id, 157, "15.3.1", games: 60, now),
                Scope(account.Id, 103, "15.3.1", games: 30, now));
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var unfiltered = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains");
        unfiltered!.Rows.Single().Dedication!.ChampionId.Should().Be(157);

        // Filtering to Ahri must re-point the score at Ahri: "most dedicated Ahri
        // players" cannot rank on an unrelated top main.
        var filtered = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains?championId=103&sort=dedication");
        var dedication = filtered!.Rows.Single().Dedication;
        dedication!.ChampionId.Should().Be(103);
        dedication.PlayRate.Should().BeApproximately(0.3d, 1e-9);
        dedication.CareerGames.Should().Be(30);
    }

    [Fact]
    public async Task Leaderboard_falls_back_to_the_rank_order_for_an_unknown_sort()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        var top = Account("top-puuid", "TopRank", "EUW1");
        var bottom = Account("bottom-puuid", "BottomRank", "EUW1");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.AddRange(top, bottom);
            db.RankSnapshots.AddRange(
                Snapshot(top, "CHALLENGER", "I", 900, now),
                Snapshot(bottom, "DIAMOND", "IV", 0, now));
            db.MainChampionStats.AddRange(
                MainStat(top, championId: 103, playRate: 0.2d, now),
                MainStat(bottom, championId: 157, playRate: 0.99d, now));
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        // A stale bookmark or a typo must render the leaderboard, not a 400.
        var response = await client.GetAsync("/truemains?sort=whatever");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<LeaderboardResponse>();
        body!.Rows.Select(r => r.Identity.GameName)
            .Should().ContainInOrder("TopRank", "BottomRank");
    }

    private static RiotAccount Account(string puuid, string gameName, string platformId)
        => new()
        {
            Id = Guid.NewGuid(),
            Puuid = puuid,
            GameName = gameName,
            TagLine = platformId,
            PlatformId = platformId,
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            LastMatchIngestAtUtc = DateTime.UtcNow,
        };

    private static RankSnapshot Snapshot(RiotAccount account, string tier, string division, int leaguePoints, DateTime now)
    {
        // Mirror the ingestion writer: the denormalised account Score is what the
        // default leaderboard ordering reads.
        account.Score = RankScore.Compute(tier, division, leaguePoints);
        return new RankSnapshot
        {
            Id = Guid.NewGuid(),
            RiotAccount = account,
            CapturedAtUtc = now,
            Tier = tier,
            Division = division,
            LeaguePoints = leaguePoints,
            Wins = 50,
            Losses = 50,
        };
    }

    private static MainChampionStat MainStat(RiotAccount account, int championId, double playRate, DateTime now)
        => new()
        {
            Id = Guid.NewGuid(),
            PlatformId = account.PlatformId,
            Puuid = account.Puuid,
            ChampionId = championId,
            TotalMatches = 50,
            ChampionMatches = (int)Math.Round(50 * playRate),
            PlayRate = playRate,
            IsMain = true,
            IsOtp = playRate >= 0.85d,
            PrimaryPosition = "MIDDLE",
            PositionBreakdown = [new PositionStat { Position = "MIDDLE", Games = 50, Rate = 1d }],
            CalculatedAtUtc = now,
        };

    private static ChampionAggregateScope Scope(Guid riotAccountId, int championId, string patch, int games, DateTime lastGameUtc)
        => new()
        {
            Id = Guid.NewGuid(),
            RiotAccountId = riotAccountId,
            ChampionId = championId,
            GameVersion = patch,
            PlatformId = "EUW1",
            QueueId = 420,
            Position = "MIDDLE",
            EloBracket = EloBracket.Diamond,
            Games = games,
            Wins = games / 2,
            Kills = games,
            Deaths = games,
            Assists = games,
            LastGameStartTimeUtc = lastGameUtc,
            AggregatedAtUtc = lastGameUtc,
        };

    private ApiWebApplicationFactory CreateFactory() => new(_fixture);

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
                // These seeds carry no participants, so the ranked-games floor is
                // disabled — it is covered by the leaderboard's own suite.
                new KeyValuePair<string, string?>("TruemainsLeaderboard:MinRankedGames", "0"),
            ]);
}
