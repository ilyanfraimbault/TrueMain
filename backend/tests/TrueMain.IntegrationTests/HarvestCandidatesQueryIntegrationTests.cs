using AwesomeAssertions;
using Data;
using Data.Entities;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class HarvestCandidatesQueryIntegrationTests
{
    private const int RankedSolo = 420;
    private const int Aram = 450;

    private readonly PostgresFixture _fixture;

    public HarvestCandidatesQueryIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetHarvestCandidatesAsync_AggregatesOrphanRows_ApplyingQueueAndThresholdFilters()
    {
        await _fixture.ResetDatabaseAsync();
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
        await SeedAsync(now);

        await using var session = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        var rows = await session.MatchParticipants.GetHarvestCandidatesAsync(
            ["KR"], RankedSolo, minObservedGames: 5, maxRows: 100, DateTime.UnixEpoch, CancellationToken.None);

        // P1 (6 orphan ranked-solo games on champ 22) is the only one above the gate.
        // Excluded: P2 (3 games < 5), P3 (tracked — RiotAccountId set), P4 (ARAM queue).
        rows.Should().ContainSingle();
        var harvested = rows.Single();
        harvested.PlatformId.Should().Be("KR");
        harvested.Puuid.Should().Be("P1");
        harvested.ChampionId.Should().Be(22);
        harvested.ObservedGames.Should().Be(6);
        harvested.ObservedWins.Should().Be(4);
        harvested.LastSeenUtc.Should().Be(now);
    }

    [Fact]
    public async Task GetHarvestCandidatesAsync_GroupsByChampion_ForSamePuuid()
    {
        await _fixture.ResetDatabaseAsync();
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

        await using (var db = _fixture.CreateDbContext())
        {
            // Same puuid, two champions, each above the gate -> two distinct rows.
            for (var i = 0; i < 5; i++)
            {
                MatchParticipantSeed.AddMatchWithParticipant(db, $"GRP_A_{i}", "KR", RankedSolo, now.AddDays(-i), "PX", 22, win: true);
                MatchParticipantSeed.AddMatchWithParticipant(db, $"GRP_B_{i}", "KR", RankedSolo, now.AddDays(-i), "PX", 64, win: false);
            }

            await db.SaveChangesAsync();
        }

        await using var session = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        var rows = await session.MatchParticipants.GetHarvestCandidatesAsync(
            ["KR"], RankedSolo, minObservedGames: 5, maxRows: 100, DateTime.UnixEpoch, CancellationToken.None);

        rows.Should().HaveCount(2);
        rows.Select(r => r.ChampionId).Should().BeEquivalentTo([22, 64]);
        rows.Should().OnlyContain(r => r.Puuid == "PX" && r.ObservedGames == 5);
    }

    [Fact]
    public async Task GetHarvestCandidatesAsync_ExcludesMatchesBeforeSinceUtc()
    {
        await _fixture.ResetDatabaseAsync();
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

        await using (var db = _fixture.CreateDbContext())
        {
            // PR: 6 recent orphan games (within the window). POLD: 6 games all older than the
            // cutoff -> excluded entirely by the date filter.
            for (var i = 0; i < 6; i++)
            {
                MatchParticipantSeed.AddMatchWithParticipant(db, $"REC_{i}", "KR", RankedSolo, now.AddDays(-i), "PR", 22, win: true);
                MatchParticipantSeed.AddMatchWithParticipant(db, $"OLD_{i}", "KR", RankedSolo, now.AddDays(-30 - i), "POLD", 22, win: true);
            }

            await db.SaveChangesAsync();
        }

        await using var session = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        var rows = await session.MatchParticipants.GetHarvestCandidatesAsync(
            ["KR"], RankedSolo, minObservedGames: 5, maxRows: 100, sinceUtc: now.AddDays(-10), CancellationToken.None);

        rows.Should().ContainSingle();
        rows.Single().Puuid.Should().Be("PR");
    }

    [Fact]
    public async Task GetHarvestCandidatesAsync_RespectsMaxRows_OrderedByObservedGamesDesc()
    {
        await _fixture.ResetDatabaseAsync();
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

        await using (var db = _fixture.CreateDbContext())
        {
            // Three eligible (puuid, champion): 8, 7 and 6 observed games respectively.
            SeedGames(db, "PA", gameCount: 8, now);
            SeedGames(db, "PB", gameCount: 7, now);
            SeedGames(db, "PC", gameCount: 6, now);
            await db.SaveChangesAsync();
        }

        await using var session = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        var rows = await session.MatchParticipants.GetHarvestCandidatesAsync(
            ["KR"], RankedSolo, minObservedGames: 5, maxRows: 2, DateTime.UnixEpoch, CancellationToken.None);

        // Truncated to the top 2 by observed games (PC with 6 drops off).
        rows.Should().HaveCount(2);
        rows.Select(r => r.ObservedGames).Should().Equal(8, 7);
        rows.Select(r => r.Puuid).Should().Equal("PA", "PB");
    }

    [Fact]
    public async Task GetHarvestCandidatesAsync_MergesAcrossPlatforms_AppliesGlobalCapAndOrdering()
    {
        await _fixture.ResetDatabaseAsync();
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

        await using (var db = _fixture.CreateDbContext())
        {
            // Eligible (puuid, champion) spread across the three harvested platforms with
            // distinct observed-game counts. The per-platform chunking must still yield the
            // same global top-N (by observed games desc) as the old single ANY(...) query:
            // KR alone can occupy the whole quota, so the cap is genuinely cross-platform.
            SeedGamesOn(db, "KR", "PA", gameCount: 8, now);   // KR, 8 games
            SeedGamesOn(db, "KR", "PB", gameCount: 7, now);   // KR, 7 games
            SeedGamesOn(db, "EUW1", "PE", gameCount: 6, now); // EUW1, 6 games
            SeedGamesOn(db, "NA1", "PN", gameCount: 5, now);  // NA1, 5 games
            await db.SaveChangesAsync();
        }

        await using var session = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        var rows = await session.MatchParticipants.GetHarvestCandidatesAsync(
            ["KR", "EUW1", "NA1"], RankedSolo, minObservedGames: 5, maxRows: 3, DateTime.UnixEpoch, CancellationToken.None);

        // Global top 3 by observed games across all platforms: PA(8,KR), PB(7,KR), PE(6,EUW1).
        // PN(5,NA1) drops off even though it is its platform's only candidate.
        rows.Should().HaveCount(3);
        rows.Select(r => r.ObservedGames).Should().Equal(8, 7, 6);
        rows.Select(r => r.Puuid).Should().Equal("PA", "PB", "PE");
        rows.Select(r => r.PlatformId).Should().Equal("KR", "KR", "EUW1");
    }

    private static void SeedGamesOn(TrueMainDbContext db, string platformId, string puuid, int gameCount, DateTime now)
    {
        for (var i = 0; i < gameCount; i++)
        {
            MatchParticipantSeed.AddMatchWithParticipant(
                db, $"{platformId}_{puuid}_{i}", platformId, RankedSolo, now.AddDays(-i), puuid, 22, win: true);
        }
    }

    private static void SeedGames(TrueMainDbContext db, string puuid, int gameCount, DateTime now)
    {
        for (var i = 0; i < gameCount; i++)
        {
            MatchParticipantSeed.AddMatchWithParticipant(
                db, $"{puuid}_{i}", "KR", RankedSolo, now.AddDays(-i), puuid, 22, win: true);
        }
    }

    private async Task SeedAsync(DateTime now)
    {
        await using var db = _fixture.CreateDbContext();

        // A tracked account so P3's participant rows can carry a non-null RiotAccountId FK.
        var trackedAccountId = Guid.NewGuid();
        db.RiotAccounts.Add(new RiotAccount
        {
            Id = trackedAccountId,
            Puuid = "P3",
            PlatformId = "KR",
            GameName = "Tracked",
            TagLine = "KR1",
            ProfileIconId = 1,
            SummonerLevel = 200,
            UpdatedAtUtc = now
        });

        // P1: 6 orphan ranked-solo games on champ 22, 4 wins, most recent at `now`.
        for (var i = 0; i < 6; i++)
        {
            MatchParticipantSeed.AddMatchWithParticipant(db, $"P1_{i}", "KR", RankedSolo, now.AddDays(-i), "P1", 22, win: i < 4);
        }

        // P2: only 3 orphan games -> below the gate.
        for (var i = 0; i < 3; i++)
        {
            MatchParticipantSeed.AddMatchWithParticipant(db, $"P2_{i}", "KR", RankedSolo, now.AddDays(-i), "P2", 22, win: true);
        }

        // P3: 6 ranked-solo games but TRACKED (RiotAccountId set) -> excluded.
        for (var i = 0; i < 6; i++)
        {
            MatchParticipantSeed.AddMatchWithParticipant(db, $"P3_{i}", "KR", RankedSolo, now.AddDays(-i), "P3", 22, win: true, riotAccountId: trackedAccountId);
        }

        // P4: 6 orphan games but in ARAM -> excluded by the queue filter.
        for (var i = 0; i < 6; i++)
        {
            MatchParticipantSeed.AddMatchWithParticipant(db, $"P4_{i}", "KR", Aram, now.AddDays(-i), "P4", 22, win: true);
        }

        await db.SaveChangesAsync();
    }
}
