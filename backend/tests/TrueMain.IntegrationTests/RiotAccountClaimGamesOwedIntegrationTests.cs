using AwesomeAssertions;
using Data.Entities;
using Data.Repositories;

namespace TrueMain.IntegrationTests;

/// <summary>
/// #1360: the claim orders by how many ranked games a player has actually played since the
/// last visit, not by how long ago that visit was. Ordering by age alone spent the batch on
/// whoever had waited longest whether or not they had played — on production that meant a
/// 27-day median revisit with a 20-game window, so the most active mains lost games between
/// visits while idle accounts consumed slots.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RiotAccountClaimGamesOwedIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Claim_PrefersTheAccountThatPlayed_OverTheOneVisitedLongestAgo()
    {
        await fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // The idle account was visited far longer ago and would win on age alone.
        await SeedMainAsync("idle", lastMatchIngestAtUtc: now.AddDays(-30), ladderGames: 500, ladderGamesAtLastIngest: 500);
        await SeedMainAsync("active", lastMatchIngestAtUtc: now.AddDays(-2), ladderGames: 540, ladderGamesAtLastIngest: 500);

        var claimed = await ClaimAsync(now, take: 1);

        claimed.Should().ContainSingle().Which.Puuid.Should().Be("puuid-active");
    }

    [Fact]
    public async Task Claim_OrdersByHowManyGamesWerePlayed()
    {
        await fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await SeedMainAsync("few", lastMatchIngestAtUtc: now.AddDays(-1), ladderGames: 505, ladderGamesAtLastIngest: 500);
        await SeedMainAsync("many", lastMatchIngestAtUtc: now.AddDays(-1), ladderGames: 560, ladderGamesAtLastIngest: 500);
        await SeedMainAsync("none", lastMatchIngestAtUtc: now.AddDays(-1), ladderGames: 500, ladderGamesAtLastIngest: 500);

        var claimed = await ClaimAsync(now, take: 3);

        claimed.Select(account => account.Puuid).Should().Equal(["puuid-many", "puuid-few", "puuid-none"]);
    }

    [Fact]
    public async Task Claim_FallsBackToAgeForAccountsWithNoLadderReading()
    {
        await fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // Below the swept tiers, or unranked: no reading ever carried wins/losses, so the
        // owed term is unknown. These must keep the age ordering they had before #1360
        // rather than sinking behind every account that has a reading.
        await SeedMainAsync("older", lastMatchIngestAtUtc: now.AddDays(-10), ladderGames: null, ladderGamesAtLastIngest: null);
        await SeedMainAsync("newer", lastMatchIngestAtUtc: now.AddDays(-1), ladderGames: null, ladderGamesAtLastIngest: null);

        var claimed = await ClaimAsync(now, take: 2);

        claimed.Select(account => account.Puuid).Should().Equal(["puuid-older", "puuid-newer"]);
    }

    [Fact]
    public async Task Claim_StillTakesNeverIngestedAccountsFirst()
    {
        await fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await SeedMainAsync("played-a-lot", lastMatchIngestAtUtc: now.AddDays(-1), ladderGames: 600, ladderGamesAtLastIngest: 500);
        await SeedMainAsync("never", lastMatchIngestAtUtc: null, ladderGames: null, ladderGamesAtLastIngest: null);

        var claimed = await ClaimAsync(now, take: 1);

        claimed.Should().ContainSingle().Which.Puuid.Should().Be("puuid-never");
    }

    [Fact]
    public async Task MarkingAnIngest_ResetsTheOwedBaselineInTheSameStatement()
    {
        await fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await SeedMainAsync("played", lastMatchIngestAtUtc: now.AddDays(-1), ladderGames: 560, ladderGamesAtLastIngest: 500);

        await using (var db = fixture.CreateDbContext())
        {
            await new RiotAccountRepository(db)
                .UpdateLastMatchIngestAtAsync("KR", "puuid-played", now, CancellationToken.None);
        }

        await using var verify = fixture.CreateDbContext();
        var account = verify.RiotAccounts.Single(row => row.Puuid == "puuid-played");

        // Without this the account reads as freshly ingested while still owing every game it
        // owed before, and the claim hands it straight back at the top of the next batch.
        account.LadderGamesAtLastIngest.Should().Be(560);
        account.LastMatchIngestAtUtc.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
    }

    private async Task<List<AccountKey>> ClaimAsync(DateTime now, int take)
    {
        await using var db = fixture.CreateDbContext();
        return await new RiotAccountRepository(db).ClaimAccountsForMatchIngestAtomicallyAsync(
            new Dictionary<string, int> { ["KR"] = take },
            take,
            // Established mains only in these fixtures, so the share does not split the batch.
            1.0,
            now,
            TimeSpan.FromMinutes(30),
            CancellationToken.None);
    }

    private async Task SeedMainAsync(
        string name,
        DateTime? lastMatchIngestAtUtc,
        int? ladderGames,
        int? ladderGamesAtLastIngest)
    {
        await using var db = fixture.CreateDbContext();
        var now = DateTime.UtcNow;
        var puuid = $"puuid-{name}";

        db.RiotAccounts.Add(new RiotAccount
        {
            Puuid = puuid,
            PlatformId = "KR",
            GameName = name,
            TagLine = "KR1",
            SummonerId = $"sum-{name}",
            ProfileIconId = 1,
            SummonerLevel = 300,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            LastMatchIngestAtUtc = lastMatchIngestAtUtc,
            LadderGames = ladderGames,
            LadderGamesAtLastIngest = ladderGamesAtLastIngest,
            MatchIngestStatus = MatchIngestStatus.Idle
        });

        db.MainChampionStats.Add(new MainChampionStat
        {
            PlatformId = "KR",
            Puuid = puuid,
            ChampionId = 22,
            TotalMatches = 30,
            ChampionMatches = 25,
            PlayRate = 0.83,
            IsMain = true,
            IsActive = true,
            IsOtp = false,
            PrimaryPosition = "BOTTOM",
            PositionBreakdown = [],
            CalculatedAtUtc = now
        });

        await db.SaveChangesAsync();
    }
}
