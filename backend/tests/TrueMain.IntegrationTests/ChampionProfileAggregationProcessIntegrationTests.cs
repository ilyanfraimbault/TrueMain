using System.Collections.Frozen;
using AwesomeAssertions;
using Core.Lol.Map;
using Core.Options;
using Data.BuildFacts;
using Data.Entities;
using Data.Statics;
using Ingestor.Options;
using Ingestor.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.TestKit;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Exercises the champion-profile fold (#1449) against real Postgres: the generated
/// thirty-column upsert, the per-match flag that makes a re-run a no-op, and the rules
/// that are specific to profiles — only participants carrying the #1448 context fields
/// count, remakes fold to nothing, lane leads need both sides' snapshots, archetypes
/// come from the final inventory, and the ranged flag is a COALESCEd static attribute.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionProfileAggregationProcessIntegrationTests
{
    private const int QueueId = 420;
    private const string Version = "16.4.521.123";
    private const string Patch = "16.4";
    private const int Aatrox = 266;
    private const int Caitlyn = 51;
    private const int CritItem = 3031;
    private const int TankItem = 3068;
    private const int ApItem = 3089;

    private readonly PostgresFixture _fixture;

    public ChampionProfileAggregationProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_FoldsTheContextSums_LaneLeads_Archetypes_AndRangedFlag()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMatchAsync("m-1", durationSeconds: 1800, withContext: true, topWins: true,
            topItems: [CritItem, TankItem], snapshots: true);
        await SeedMatchAsync("m-2", durationSeconds: 2100, withContext: true, topWins: false,
            topItems: [CritItem], snapshots: true);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var top = await db.ChampionProfileStats.AsNoTracking()
            .SingleAsync(p => p.ChampionId == Aatrox && p.Position == "TOP");

        top.Patch.Should().Be(Patch);
        top.Games.Should().Be(2);
        top.Wins.Should().Be(1);
        top.GameDurationSecondsSum.Should().Be(3900);
        top.PhysicalDamageToChampionsSum.Should().Be(2 * 18_000);
        top.MagicDamageToChampionsSum.Should().Be(2 * 2_000);
        top.TrueDamageToChampionsSum.Should().Be(2 * 500);
        top.TotalHealSum.Should().Be(2 * 3_000);
        top.TimeCCingOthersSum.Should().Be(2 * 30);
        top.DamageTakenSum.Should().Be(2 * 20_000);
        // Five teammates at 20 000 each, both games.
        top.TeamDamageTakenGames.Should().Be(2);
        top.TeamDamageTakenSum.Should().Be(2 * 5 * 20_000);
        // TOP leads its opponent by 300 gold and 150 XP at 10, by 600 and 300 at 15.
        top.LaneGamesAt10.Should().Be(2);
        top.GoldLeadAt10Sum.Should().Be(2 * 300);
        top.XpLeadAt10Sum.Should().Be(2 * 150);
        top.KillsBy10Sum.Should().Be(2 * 2);
        top.LaneGamesAt15.Should().Be(2);
        top.GoldLeadAt15Sum.Should().Be(2 * 600);
        top.XpLeadAt15Sum.Should().Be(2 * 300);
        top.ItemGames.Should().Be(2);
        top.CritGames.Should().Be(2);
        top.TankGames.Should().Be(1);
        top.AbilityPowerGames.Should().Be(0);
        top.IsRanged.Should().BeFalse();

        var bottom = await db.ChampionProfileStats.AsNoTracking()
            .SingleAsync(p => p.ChampionId == Caitlyn && p.Position == "BOTTOM");
        bottom.IsRanged.Should().BeTrue();
        bottom.AbilityPowerGames.Should().Be(2, "the seeded bot lane completes an AP item");

        // Ten canonical participants per match, every one of them measured: ten rows.
        (await db.ChampionProfileStats.CountAsync()).Should().Be(10);
        (await db.Matches.CountAsync(m => m.ProfileAggregated)).Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_FlagsButDoesNotFold_MatchesWithoutContextFields_OrRemakes()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMatchAsync("legacy", durationSeconds: 1800, withContext: false, topWins: true, topItems: [], snapshots: true);
        await SeedMatchAsync("remake", durationSeconds: 200, withContext: true, topWins: true, topItems: [], snapshots: false);

        var summary = await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        (await db.ChampionProfileStats.CountAsync()).Should().Be(0, "a pre-#1448 row and a remake both fold to nothing");
        (await db.Matches.CountAsync(m => m.ProfileAggregated)).Should().Be(2, "both are still flagged as done");
        summary.Should().BeEquivalentTo(new { Matches = 2, Participants = 0, Rows = 0 }, o => o.ExcludingMissingMembers());
    }

    [Fact]
    public async Task RunAsync_IsANoOpOnASecondRun_AndKeepsTheRangedFlagWhenStaticsAreDown()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMatchAsync("m-1", durationSeconds: 1800, withContext: true, topWins: true, topItems: [], snapshots: true);
        await CreateProcess().RunCoreAsync(CancellationToken.None);

        // A second match arrives while Data Dragon is unreachable: its sums fold, the
        // flag set by the first run is kept rather than blanked.
        await SeedMatchAsync("m-2", durationSeconds: 1800, withContext: true, topWins: true, topItems: [], snapshots: true);
        await CreateProcess(staticsAvailable: false).RunCoreAsync(CancellationToken.None);
        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var top = await db.ChampionProfileStats.AsNoTracking()
            .SingleAsync(p => p.ChampionId == Aatrox && p.Position == "TOP");
        top.Games.Should().Be(2, "the third run found nothing pending");
        top.IsRanged.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_CountsALaneOnlyWhenBothSidesHaveTheSnapshot()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedMatchAsync("short", durationSeconds: 700, withContext: true, topWins: true, topItems: [], snapshots: false);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var top = await db.ChampionProfileStats.AsNoTracking()
            .SingleAsync(p => p.ChampionId == Aatrox && p.Position == "TOP");
        top.Games.Should().Be(1);
        top.LaneGamesAt10.Should().Be(0);
        top.LaneGamesAt15.Should().Be(0);
        top.ItemGames.Should().Be(1, "the inventory is still classified — it holds no completed item");
    }

    private ChampionProfileAggregationProcess CreateProcess(bool staticsAvailable = true)
        => new(
            NullLogger<ChampionProfileAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions { QueueId = LolQueueId.RankedSoloDuo }),
            Microsoft.Extensions.Options.Options.Create(new ChampionProfileAggregationOptions()),
            new FakeItemMetadataProvider(),
            new FakeChampionStaticsProvider(staticsAvailable),
            new TestDbContextFactory(_fixture),
            TimeProvider.System);

    /// <summary>
    /// Ten participants on the five canonical positions: team 100 is Aatrox TOP, Caitlyn
    /// BOTTOM and three fillers; team 200 mirrors it with other champions. Every measured
    /// participant carries the same context figures, so sums are multiples of them.
    /// </summary>
    private async Task SeedMatchAsync(
        string matchId, int durationSeconds, bool withContext, bool topWins, int[] topItems, bool snapshots)
    {
        await using var db = _fixture.CreateDbContext();

        db.Matches.Add(new MatchBuilder()
            .WithId(matchId)
            .WithQueueId(QueueId)
            .WithGameVersion(Version)
            .WithGameDurationSeconds(durationSeconds)
            .WithTimelineIngested()
            .Build());

        string[] positions = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];
        int[] blueChampions = [Aatrox, 64, 103, Caitlyn, 412];
        int[] redChampions = [86, 60, 238, 222, 555];

        for (var slot = 0; slot < 5; slot++)
        {
            var blueItems = slot == 0 ? topItems : slot == 3 ? [ApItem] : [];
            db.MatchParticipants.Add(Participant(matchId, slot + 1, blueChampions[slot], 100, positions[slot], topWins, withContext, blueItems));
            db.MatchParticipants.Add(Participant(matchId, slot + 6, redChampions[slot], 200, positions[slot], !topWins, withContext, []));
        }

        if (snapshots)
        {
            foreach (var minute in new[] { 10, 15 })
            {
                var lead = minute == 10 ? 300 : 600;
                for (var slot = 0; slot < 5; slot++)
                {
                    db.MatchParticipantTimelineSnapshots.Add(Snapshot(matchId, slot + 1, minute, gold: 3000 + lead, xp: 2000 + lead / 2, kills: 2));
                    db.MatchParticipantTimelineSnapshots.Add(Snapshot(matchId, slot + 6, minute, gold: 3000, xp: 2000, kills: 0));
                }
            }
        }

        await db.SaveChangesAsync();
    }

    private static MatchParticipant Participant(
        string matchId, int participantId, int championId, int teamId, string position, bool win, bool withContext, int[] items)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            Puuid = $"puuid-{matchId}-{participantId}",
            SummonerName = "seed",
            SummonerLevel = 100,
            ChampionId = championId,
            TeamId = teamId,
            TeamPosition = position,
            IndividualPosition = position,
            Lane = position,
            Role = "SOLO",
            Win = win,
            ChampLevel = 16,
            Item0 = items.Length > 0 ? items[0] : 0,
            Item1 = items.Length > 1 ? items[1] : 0,
            Item6 = 3363,
            TrinketItemId = 3363,
            ItemEvents = [],
            SkillEvents = [],
            PhysicalDamageDealtToChampions = withContext ? 18_000 : null,
            MagicDamageDealtToChampions = withContext ? 2_000 : null,
            TrueDamageDealtToChampions = withContext ? 500 : null,
            TotalHeal = withContext ? 3_000 : null,
            TotalHealsOnTeammates = withContext ? 400 : null,
            TotalDamageShieldedOnTeammates = withContext ? 200 : null,
            TimeCCingOthers = withContext ? 30 : null,
            TotalTimeCCDealt = withContext ? 150 : null,
            TotalDamageTaken = withContext ? 20_000 : null,
            DamageSelfMitigated = withContext ? 12_000 : null,
        };

    private static MatchParticipantTimelineSnapshot Snapshot(string matchId, int participantId, int minute, int gold, int xp, int kills)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            IntervalMinute = minute,
            TimestampMs = minute * 60_000,
            TotalGold = gold,
            MinionsKilled = minute * 6,
            JungleMinionsKilled = 0,
            Level = minute / 2 + 3,
            Xp = xp,
            Kills = kills,
            DamageToChampions = minute * 300,
            WardsPlaced = 0,
            WardsKilled = 0
        };

    private sealed class FakeItemMetadataProvider : IItemMetadataProvider
    {
        private static readonly IReadOnlyDictionary<int, ItemMetadata> Metadata = new Dictionary<int, ItemMetadata>
        {
            [CritItem] = Final(CritItem, "Damage", "CriticalStrike"),
            [TankItem] = Final(TankItem, "Health", "Armor", "AbilityHaste"),
            [ApItem] = Final(ApItem, "SpellDamage"),
        };

        public Task<IReadOnlyDictionary<int, ItemMetadata>> GetItemsAsync(string gameVersion, CancellationToken ct)
            => Task.FromResult(Metadata);

        private static ItemMetadata Final(int id, params string[] categories)
            => new(id, 3000, true, false, false, false, true, false)
            {
                Categories = categories.ToFrozenSet(StringComparer.Ordinal),
            };
    }

    private sealed class FakeChampionStaticsProvider(bool available) : IChampionStaticsProvider
    {
        public Task<IReadOnlyDictionary<int, ChampionStatics>> GetChampionsAsync(string gameVersion, CancellationToken ct)
        {
            if (!available)
            {
                throw new HttpRequestException("Data Dragon is down.");
            }

            IReadOnlyDictionary<int, ChampionStatics> statics = new Dictionary<int, ChampionStatics>
            {
                [Aatrox] = new(Aatrox, "Aatrox", 175),
                [Caitlyn] = new(Caitlyn, "Caitlyn", 650),
            };
            return Task.FromResult(statics);
        }
    }
}
