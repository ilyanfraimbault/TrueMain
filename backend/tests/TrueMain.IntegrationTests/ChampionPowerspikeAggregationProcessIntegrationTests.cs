using AwesomeAssertions;
using Core.Lol.Map;
using Core.Options;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ChampionPowerspikeAggregationProcessIntegrationTests
{
    private const int QueueId = 420;
    private const int Champion = 157; // Yone
    private const int Opponent = 238; // Zed
    private const string Position = "MIDDLE";
    private const string EloBracket = "GOLD";
    private const int Games = 6;
    private const string Version = "16.4.521.123";

    // Per-game leads (game i leads by Base + i), so the lead varies across games and
    // the global spread σ is non-zero — otherwise power would be undefined.
    private const int GoldBase = 500;
    private const int DamageBase = 300;

    private const int CoreItemId = 3157; // Zhonya's Hourglass — legendary, non-boots
    private const int ItemPurchaseMinute = 14;

    private readonly PostgresFixture _fixture;

    public ChampionPowerspikeAggregationProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_AggregatesCurveAndSigmaAndIsIdempotent()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedGamesAsync();

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);
        // A second run must find nothing pending (every match is flagged) and must
        // not double the additive totals.
        await process.RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        // One curve row per dense minute (1..30); each sums all six games' leads.
        var curve = await db.ChampionPowerspikeCurveStats.AsNoTracking()
            .OrderBy(s => s.IntervalMinute)
            .ToListAsync();
        curve.Select(c => c.IntervalMinute).Should().Equal(Enumerable.Range(1, 30));

        var expectedGold = Enumerable.Range(0, Games).Sum(i => GoldBase + i);       // 3015
        var expectedDamage = Enumerable.Range(0, Games).Sum(i => DamageBase + i);   // 1815
        foreach (var point in curve)
        {
            point.ChampionId.Should().Be(Champion);
            point.TeamPosition.Should().Be(Position);
            point.Patch.Should().Be("16.4");
            point.EloBracket.Should().Be(EloBracket);
            point.Games.Should().Be(Games, "the second run must not double the count");
            point.TotalGoldDiff.Should().Be(expectedGold);
            point.TotalDamageDiff.Should().Be(expectedDamage);
        }

        // Sigma: one row per minute, two directed pairs per game (a→b and b→a), so
        // the signed sums cancel to zero and the sample count is 2·games.
        var sigma = await db.PowerspikeSigmaStats.AsNoTracking()
            .OrderBy(s => s.IntervalMinute)
            .ToListAsync();
        sigma.Select(s => s.IntervalMinute).Should().Equal(Enumerable.Range(1, 30));
        foreach (var row in sigma)
        {
            row.QueueId.Should().Be(QueueId);
            row.SampleCount.Should().Be(2L * Games);
            row.SumGoldDiff.Should().BeApproximately(0, 1e-6);
            row.SumDamageDiff.Should().BeApproximately(0, 1e-6);
            row.SumGoldDiffSq.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task RunAsync_AggregatesLevelAndItemEventSpikes()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedGamesAsync();

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        var events = await db.ChampionPowerspikeEventStats.AsNoTracking().ToListAsync();

        // Three level milestones + one completed item.
        events.Select(e => (e.EventType, e.RefId)).Should().BeEquivalentTo(
            [("level", 6), ("level", 11), ("level", 16), ("item", CoreItemId)]);

        foreach (var row in events)
        {
            row.ChampionId.Should().Be(Champion);
            row.TeamPosition.Should().Be(Position);
            row.Patch.Should().Be("16.4");
            row.EloBracket.Should().Be(EloBracket);
            row.Games.Should().Be(Games);
            // The lead is flat across minutes, so power is flat and the slope-change
            // spike is exactly zero — a clean check that the spike is computed (not
            // null) at each detected event minute.
            row.SumSpike.Should().BeApproximately(0, 1e-9);
        }

        // Level milestones are first reached at these minutes in every game, so the
        // averaged minute (SumMinute / Games) pins them.
        AvgMinute(events, "level", 6).Should().Be(10);
        AvgMinute(events, "level", 11).Should().Be(15);
        AvgMinute(events, "level", 16).Should().Be(20);
        AvgMinute(events, "item", CoreItemId).Should().Be(ItemPurchaseMinute);
    }

    private static double AvgMinute(IReadOnlyList<ChampionPowerspikeEventStat> events, string type, int refId)
    {
        var row = events.Single(e => e.EventType == type && e.RefId == refId);
        return row.SumMinute / row.Games;
    }

    private ChampionPowerspikeAggregationProcess CreateProcess()
        => new(
            NullLogger<ChampionPowerspikeAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(new PowerspikeAggregationOptions()),
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions { QueueId = LolQueueId.RankedSoloDuo }),
            new TestDbContextFactory(_fixture),
            TimeProvider.System);

    private async Task SeedGamesAsync()
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("SpikeMain")
            .WithTagLine("KR1")
            .WithPuuid("spike-main-puuid")
            .Build();
        db.RiotAccounts.Add(account);

        for (var i = 0; i < Games; i++)
        {
            var matchId = $"ps-{Version}-{i}";
            db.Matches.Add(new MatchBuilder()
                .WithId(matchId)
                .WithQueueId(QueueId)
                .WithGameVersion(Version)
                .WithTimelineIngested()
                .Build());

            db.MatchParticipants.Add(Participant(matchId, participantId: 1, Champion, teamId: 100,
                riotAccountId: account.Id, coreItem: true, purchaseMs: ItemPurchaseMinute * 60_000));
            db.MatchParticipants.Add(Participant(matchId, participantId: 2, Opponent, teamId: 200,
                riotAccountId: null, coreItem: false, purchaseMs: null));

            for (var minute = 1; minute <= 30; minute++)
            {
                var p1Gold = 10_000 + minute * 100;
                var p1Damage = 5_000 + minute * 50;
                var level = LevelAt(minute);

                db.MatchParticipantTimelineSnapshots.Add(
                    Snapshot(matchId, 1, minute, p1Gold, p1Damage, level));
                db.MatchParticipantTimelineSnapshots.Add(
                    Snapshot(matchId, 2, minute, p1Gold - (GoldBase + i), p1Damage - (DamageBase + i), level));
            }
        }

        await db.SaveChangesAsync();
    }

    private static int LevelAt(int minute) => minute switch
    {
        >= 20 => 16,
        >= 15 => 11,
        >= 10 => 6,
        _ => 5
    };

    private static MatchParticipant Participant(
        string matchId, int participantId, int championId, int teamId,
        Guid? riotAccountId, bool coreItem, int? purchaseMs)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            Puuid = $"puuid-{matchId}-{participantId}",
            RiotAccountId = riotAccountId,
            SummonerName = "seed",
            SummonerLevel = 100,
            ChampionId = championId,
            TeamId = teamId,
            TeamPosition = Position,
            IndividualPosition = Position,
            Lane = Position,
            Role = "SOLO",
            EloBracket = EloBracket,
            Win = participantId == 1,
            ChampLevel = 16,
            Item0 = coreItem ? CoreItemId : 0,
            ItemEvents = coreItem && purchaseMs is not null
                ? [new ItemEvent { TimestampMs = purchaseMs.Value, EventType = "ITEM_PURCHASED", ItemId = CoreItemId }]
                : [],
            SkillEvents = []
        };

    private static MatchParticipantTimelineSnapshot Snapshot(
        string matchId, int participantId, int minute, int gold, int damage, int level)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            IntervalMinute = minute,
            TimestampMs = minute * 60_000,
            TotalGold = gold,
            MinionsKilled = minute * 5,
            JungleMinionsKilled = 0,
            Level = level,
            Xp = minute * 250,
            Kills = 0,
            DamageToChampions = damage,
            WardsPlaced = 0,
            WardsKilled = 0
        };
}
