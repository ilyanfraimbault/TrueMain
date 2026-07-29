using AwesomeAssertions;
using Core.Lol.Map;
using Core.Lol.Ranking;
using Core.Options;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.TestKit;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Exercises the ban fold (#920) against real Postgres — the two <c>ON CONFLICT</c>
/// upserts and the per-match flag that makes a re-run a no-op — plus the arithmetic
/// that is specific to bans: a stored <c>ALL</c> band that is deliberately NOT the
/// sum of the per-tier bands, and a denominator that counts matches which banned
/// nothing.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionBanAggregationProcessIntegrationTests
{
    private const int QueueId = 420;
    private const int Banned = 266;      // Aatrox, banned in the seeded games.
    private const int NeverBanned = 350; // Yuumi, played but never banned.
    private const string Patch = "16.4";
    private const string RawVersion = "16.4.521.123";

    private readonly PostgresFixture _fixture;

    public ChampionBanAggregationProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_CountsABanOncePerMatch_AcrossTheAllBandAndEachPlayerBand()
    {
        await _fixture.ResetDatabaseAsync();
        // Both teams ban Aatrox in every match: two ban rows, one banned match.
        await SeedMatchesAsync(count: 4, bands: [EloBracket.Gold], bans: [(100, Banned), (200, Banned)]);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var stats = await db.ChampionBanStats.AsNoTracking().ToListAsync();

        stats.Should().HaveCount(2, "one row for ALL and one for the players' band");
        stats.Should().AllSatisfy(stat =>
        {
            stat.ChampionId.Should().Be(Banned);
            stat.Patch.Should().Be(Patch, "the raw GameVersion folds to major.minor");
            stat.Bans.Should().Be(4, "a champion banned twice in a match is still one banned match");
        });
        stats.Select(stat => stat.EloBracket).Should().BeEquivalentTo([EloBracket.All, EloBracket.Gold]);
    }

    [Fact]
    public async Task RunAsync_CountsBanlessMatchesInTheDenominator()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMatchesAsync(count: 3, bands: [EloBracket.Gold], bans: [(100, Banned)]);
        await SeedMatchesAsync(count: 7, bands: [EloBracket.Gold], bans: [], matchPrefix: "clean");

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        var total = await db.BanScopeTotals.AsNoTracking().SingleAsync(t => t.EloBracket == EloBracket.All);
        total.Matches.Should().Be(10, "a match nobody banned in is exactly what the denominator measures");

        var stat = await db.ChampionBanStats.AsNoTracking().SingleAsync(s => s.EloBracket == EloBracket.All);
        stat.Bans.Should().Be(3);

        // The read divides these two: 3/10, not 3/3.
        (await db.ChampionBanStats.AsNoTracking().AnyAsync(s => s.ChampionId == NeverBanned))
            .Should().BeFalse("a champion nobody banned gets no row; the read reads that as 0, not unknown");
    }

    [Fact]
    public async Task RunAsync_StoresAllBandSeparately_BecauseBandsOverlapAndCannotBeSummed()
    {
        await _fixture.ResetDatabaseAsync();
        // One match, two tracked players in different bands: it counts once in
        // GOLD, once in PLATINUM and once in ALL. Summing the bands would say two
        // matches; only the stored ALL row says one.
        await SeedMatchesAsync(count: 1, bands: [EloBracket.Gold, EloBracket.Platinum], bans: [(100, Banned)]);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var totals = await db.BanScopeTotals.AsNoTracking().ToDictionaryAsync(t => t.EloBracket, t => t.Matches);

        totals[EloBracket.All].Should().Be(1);
        totals[EloBracket.Gold].Should().Be(1);
        totals[EloBracket.Platinum].Should().Be(1);
        (totals[EloBracket.Gold] + totals[EloBracket.Platinum])
            .Should().NotBe(totals[EloBracket.All], "this inequality is the reason ALL is a stored row");
    }

    [Fact]
    public async Task RunAsync_FoldsIntoTheAllBandOnly_WhenNoParticipantHasBeenEloStamped()
    {
        await _fixture.ResetDatabaseAsync();
        // Elo enrichment defers participants with no rank snapshot, leaving the
        // band blank. Those matches must still be counted, in ALL alone.
        await SeedMatchesAsync(count: 5, bands: [], bans: [(100, Banned)]);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        var totals = await db.BanScopeTotals.AsNoTracking().ToListAsync();
        totals.Should().ContainSingle().Which.EloBracket.Should().Be(EloBracket.All);
        totals[0].Matches.Should().Be(5);
    }

    [Fact]
    public async Task RunAsync_DoesNotDoubleCountOnRerunWithNoNewMatches()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMatchesAsync(count: 6, bands: [EloBracket.Gold], bans: [(100, Banned)]);

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);
        await process.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        (await db.ChampionBanStats.AsNoTracking().MaxAsync(s => s.Bans))
            .Should().Be(6, "counts must not double on a second run with nothing pending");
        (await db.BanScopeTotals.AsNoTracking().MaxAsync(t => t.Matches)).Should().Be(6);
        (await db.Matches.CountAsync(m => !m.BansAggregated))
            .Should().Be(0, "every seeded match was folded in on the first run");
    }

    [Fact]
    public async Task RunAsync_AccumulatesAcrossRunsAsNewMatchesArrive()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMatchesAsync(count: 6, bands: [EloBracket.Gold], bans: [(100, Banned)]);

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);

        await SeedMatchesAsync(count: 4, bands: [EloBracket.Gold], bans: [], matchPrefix: "m2");
        await process.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        var stat = await db.ChampionBanStats.AsNoTracking().SingleAsync(s => s.EloBracket == EloBracket.All);
        stat.Bans.Should().Be(6, "the second batch banned nobody, so the numerator is unchanged");

        var total = await db.BanScopeTotals.AsNoTracking().SingleAsync(t => t.EloBracket == EloBracket.All);
        total.Matches.Should().Be(10, "but the denominator grew — the ban rate must fall from 6/6 to 6/10");
    }

    [Fact]
    public async Task RunAsync_SkipsMatchesFromOtherQueues()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMatchesAsync(count: 3, bands: [EloBracket.Gold], bans: [(100, Banned)], queueId: 450);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        (await db.BanScopeTotals.CountAsync()).Should().Be(0);
        (await db.Matches.CountAsync(m => !m.BansAggregated))
            .Should().Be(3, "an out-of-queue match is left pending, exactly as the other folds leave it");
    }

    private ChampionBanAggregationProcess CreateProcess()
        => new(
            NullLogger<ChampionBanAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions { QueueId = LolQueueId.RankedSoloDuo }),
            Microsoft.Extensions.Options.Options.Create(new BanAggregationOptions()),
            new TestDbContextFactory(_fixture),
            TimeProvider.System);

    /// <summary>
    /// Seeds matches with one participant per requested elo band (so the match is
    /// folded into each of them) plus the given bans. An empty <paramref name="bands"/>
    /// seeds a single participant with a blank band, the shape elo enrichment leaves
    /// behind when it has no rank snapshot to work from.
    /// </summary>
    private async Task SeedMatchesAsync(
        int count,
        IReadOnlyList<string> bands,
        IReadOnlyList<(int TeamId, int ChampionId)> bans,
        string matchPrefix = "m",
        int queueId = QueueId)
    {
        await using var db = _fixture.CreateDbContext();

        for (var i = 0; i < count; i++)
        {
            var matchId = $"{matchPrefix}-{i}";
            db.Matches.Add(new MatchBuilder()
                .WithId(matchId)
                .WithQueueId(queueId)
                .WithGameVersion(RawVersion)
                .Build());

            var participantBands = bands.Count > 0 ? bands : [string.Empty];
            for (var p = 0; p < participantBands.Count; p++)
            {
                db.MatchParticipants.Add(new MatchParticipant
                {
                    MatchId = matchId,
                    ParticipantId = p + 1,
                    Puuid = $"puuid-{matchId}-{p}",
                    SummonerName = "seed",
                    SummonerLevel = 100,
                    ChampionId = NeverBanned,
                    TeamId = 100,
                    TeamPosition = "UTILITY",
                    IndividualPosition = "UTILITY",
                    Lane = "UTILITY",
                    Role = "SUPPORT",
                    Win = true,
                    ChampLevel = 16,
                    EloBracket = participantBands[p],
                    Item6 = 3363,
                    TrinketItemId = 3363,
                    ItemEvents = [],
                    SkillEvents = []
                });
            }

            var pickTurn = 1;
            foreach (var (teamId, championId) in bans)
            {
                db.MatchBans.Add(new MatchBan
                {
                    MatchId = matchId,
                    TeamId = teamId,
                    PickTurn = pickTurn++,
                    ChampionId = championId
                });
            }
        }

        await db.SaveChangesAsync();
    }
}
