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
    public async Task Claim_TreatsAnUnknownBaselineAsOwingNothing()
    {
        await fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // The transitional state right after the deploy: the ladder sweep has filled in
        // LadderGames, but the account has not been ingested since, so there is no baseline
        // to subtract. Reading the missing baseline as zero would report the player's whole
        // season as owed — for every tracked account at once — and sort the pool by career
        // volume instead of by recent activity. Exercised through the claim, not just the
        // rule, so the SQL translation of the ternary is covered too.
        await SeedMainAsync("no-baseline", lastMatchIngestAtUtc: now.AddDays(-1), ladderGames: 900, ladderGamesAtLastIngest: null);
        await SeedMainAsync("played-a-little", lastMatchIngestAtUtc: now.AddDays(-1), ladderGames: 510, ladderGamesAtLastIngest: 500);

        var claimed = await ClaimAsync(now, take: 2);

        claimed.Select(account => account.Puuid).Should().Equal(["puuid-played-a-little", "puuid-no-baseline"]);
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
    public async Task Claim_TreatsASeasonResetAsNoGamesOwed_RatherThanAsNegative()
    {
        await fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // A Riot season reset restarts wins/losses from the bottom, so the raw difference
        // goes negative for every account at once. Floored at zero, such an account is
        // merely "owes nothing" and keeps its place in the age ordering; unfloored it would
        // sort behind accounts that genuinely owe nothing.
        await SeedMainAsync("reset", lastMatchIngestAtUtc: now.AddDays(-20), ladderGames: 12, ladderGamesAtLastIngest: 800);
        await SeedMainAsync("steady", lastMatchIngestAtUtc: now.AddDays(-1), ladderGames: 300, ladderGamesAtLastIngest: 300);

        var claimed = await ClaimAsync(now, take: 2);

        claimed.Select(account => account.Puuid).Should().Equal(["puuid-reset", "puuid-steady"]);
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
