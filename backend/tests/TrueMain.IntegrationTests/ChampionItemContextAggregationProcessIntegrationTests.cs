using System.Collections.Frozen;
using AwesomeAssertions;
using Core.Lol.Map;
using Core.Options;
using Data;
using Data.BuildFacts;
using Data.Entities;
using Data.ItemContext;
using Ingestor.Options;
using Ingestor.Processes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.TestKit;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Exercises the situational item-context fold (#1450) against real Postgres: the two
/// additive upserts, the per-match flag, the whitelist applied at fold time, and the
/// verdict rebuild that turns the counters into what the page reads.
///
/// The seeded world is deliberately extreme so the arithmetic is checkable by hand: the
/// champion faces a magic-damage team in half its games and a physical-damage team in the
/// other half, and it completes the magic-resist item in exactly the first half.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionItemContextAggregationProcessIntegrationTests
{
    private const int QueueId = 420;
    private const string Version = "16.4.521.123";
    private const string Patch = "16.4";
    private const string Position = "TOP";

    private const int Champion = 266;
    private const int MagicResistItem = 3065;
    private const int ArmourItem = 3068;
    private const int CoreItem = 6632;

    // Two enemy rosters with opposite damage profiles, and one ally roster.
    private static readonly int[] MagicEnemies = [99, 103, 134, 143, 63];
    private static readonly int[] PhysicalEnemies = [22, 51, 64, 11, 24];
    private static readonly int[] Allies = [64, 103, 51, 412];

    private readonly PostgresFixture _fixture;

    public ChampionItemContextAggregationProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_CountsEachItemInTheBucketOfItsGame_AndOnlyOnWhitelistedAxes()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync(magicGames: 12, physicalGames: 12);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        var totals = await db.ChampionItemContextTotals.AsNoTracking()
            .Where(row => row.Slot == ItemContextSlot.Build)
            .ToListAsync();

        totals.Single(row => row.Axis == ItemContextAxis.Overall).Games
            .Should().Be(24, "every folded game counts once towards the slot");
        totals.Single(row => row.Axis == ItemContextAxis.EnemyMagicDamage && row.Bucket == ItemContextBucket.High)
            .Games.Should().Be(12);
        totals.Single(row => row.Axis == ItemContextAxis.EnemyMagicDamage && row.Bucket == ItemContextBucket.Low)
            .Games.Should().Be(12);

        var magicResist = await db.ChampionItemContextStats.AsNoTracking()
            .Where(row => row.ItemId == MagicResistItem)
            .ToListAsync();

        magicResist.Single(row => row.Axis == ItemContextAxis.Overall).Games.Should().Be(12);
        magicResist.Single(row => row.Axis == ItemContextAxis.EnemyMagicDamage && row.Bucket == ItemContextBucket.High)
            .Games.Should().Be(12);
        magicResist.Should().NotContain(row => row.Axis == ItemContextAxis.EnemyMagicDamage && row.Bucket == ItemContextBucket.Low,
            "it was never built against a physical team, so that end has a zero and no row");
        magicResist.Should().NotContain(row => row.Axis == ItemContextAxis.EnemyMelee,
            "the whitelist is applied at fold time — magic resistance never answers a melee count");
        magicResist.Should().NotContain(row => row.Axis == ItemContextAxis.EnemyCrowdControl);

        // The armour item is the mirror image, and the core item is in every game.
        var armour = await db.ChampionItemContextStats.AsNoTracking()
            .Where(row => row.ItemId == ArmourItem && row.Axis == ItemContextAxis.EnemyPhysicalDamage)
            .ToListAsync();
        armour.Single(row => row.Bucket == ItemContextBucket.High).Games.Should().Be(12);

        (await db.Matches.CountAsync(m => m.ItemContextAggregated)).Should().Be(24);
    }

    [Fact]
    public async Task RunAsync_WritesTheVerdictThePageReads()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync(magicGames: 12, physicalGames: 12);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var verdicts = await db.ChampionItemContextVerdicts.AsNoTracking().ToListAsync();

        var core = verdicts.Single(v => v.ItemId == CoreItem);
        core.Class.Should().Be(ItemContextClass.Core, "it is built in every game, so no situation explains it");
        core.PickRate.Should().Be(1d);
        core.Axes.Should().BeEmpty();

        var magicResist = verdicts.Single(v => v.ItemId == MagicResistItem);
        magicResist.Class.Should().Be(ItemContextClass.Situational);
        magicResist.Games.Should().Be(12);
        magicResist.SlotGames.Should().Be(24);
        magicResist.PickRate.Should().Be(0.5d);
        magicResist.PatchWindow.Should().Be(1);

        // Two axes, because the item is whitelisted for both and the seeded lane opponent
        // is one of the magic-damage enemies: the team's damage type and the lane
        // opponent's are separately true here, and both are measured.
        magicResist.Axes.Select(finding => finding.Axis).Should().BeEquivalentTo(
            [ItemContextAxis.EnemyMagicDamage, ItemContextAxis.OpponentMagicDamage]);

        var finding = magicResist.Axes.Single(f => f.Axis == ItemContextAxis.EnemyMagicDamage);
        finding.Bucket.Should().Be(ItemContextBucket.High);
        finding.GamesIn.Should().Be(12);
        finding.TotalIn.Should().Be(12);
        finding.GamesOut.Should().Be(0);
        finding.TotalOut.Should().Be(12);
        finding.Lift.Should().Be(1d);
        finding.PatchWindow.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_IsANoOpOnASecondRun()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync(magicGames: 12, physicalGames: 12);

        await CreateProcess().RunCoreAsync(CancellationToken.None);
        var second = await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        (await db.ChampionItemContextTotals.AsNoTracking()
            .SingleAsync(row => row.Slot == ItemContextSlot.Build && row.Axis == ItemContextAxis.Overall))
            .Games.Should().Be(24, "nothing was pending, so nothing was counted twice");
        second.Should().BeEquivalentTo(new { Matches = 0, Verdicts = 0 }, o => o.ExcludingMissingMembers());
    }

    [Fact]
    public async Task RunAsync_FoldsNothingForAChampionItHasNoProfilesFor()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAsync(magicGames: 12, physicalGames: 12, seedProfiles: false);

        await CreateProcess().RunCoreAsync(CancellationToken.None);

        await using var db = _fixture.CreateDbContext();

        // The games are still counted — an item's pick rate does not need a qualified draft —
        // but not one draft-time situation could be evaluated, so none of them carries a
        // single game. The in-game axis survives: it is read from the timeline, not from a
        // profile.
        (await db.ChampionItemContextTotals.AsNoTracking()
            .SingleAsync(row => row.Slot == ItemContextSlot.Build && row.Axis == ItemContextAxis.Overall))
            .Games.Should().Be(24);

        var axes = await db.ChampionItemContextTotals.AsNoTracking()
            .Where(row => row.Axis != ItemContextAxis.Overall)
            .Select(row => row.Axis)
            .Distinct()
            .ToListAsync();
        axes.Should().Equal([ItemContextAxis.OwnGoldLeadAt15]);

        var verdict = await db.ChampionItemContextVerdicts.AsNoTracking()
            .SingleAsync(v => v.ItemId == MagicResistItem);
        verdict.Class.Should().Be(ItemContextClass.Preference,
            "an unqualifiable draft leaves an honest 'nothing moves this', not a guess");
    }

    private ChampionItemContextAggregationProcess CreateProcess()
        => new(
            NullLogger<ChampionItemContextAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions { QueueId = LolQueueId.RankedSoloDuo }),
            Microsoft.Extensions.Options.Options.Create(new ItemContextAggregationOptions
            {
                // The seeded world is 24 games, so the floors move down with it; the rules
                // they gate are the same ones production runs.
                MinBucketGames = 5,
                MinProfileGames = 1,
                MinPickRate = 0.05,
            }),
            new FakeItemMetadataProvider(),
            new TestDbContextFactory(_fixture),
            TimeProvider.System);

    private async Task SeedAsync(int magicGames, int physicalGames, bool seedProfiles = true)
    {
        await using var db = _fixture.CreateDbContext();

        var account = new RiotAccountBuilder()
            .WithGameName("ContextMain").WithTagLine("KR1").WithPuuid("context-main-puuid").Build();
        db.RiotAccounts.Add(account);
        db.MainChampionStats.Add(new MainChampionStat
        {
            PlatformId = account.PlatformId,
            Puuid = account.Puuid,
            ChampionId = Champion,
            TotalMatches = magicGames + physicalGames,
            ChampionMatches = magicGames + physicalGames,
            PlayRate = 1.0,
            IsMain = true,
            PrimaryPosition = Position,
            CalculatedAtUtc = DateTime.UtcNow,
        });

        if (seedProfiles)
        {
            foreach (var championId in MagicEnemies)
            {
                db.ChampionProfileStats.Add(Profile(championId, magicShare: 0.90, physicalShare: 0.05));
            }

            var physical = PhysicalEnemies.Concat(Allies).Concat([Champion])
                .Distinct()
                .Except(MagicEnemies);
            foreach (var championId in physical)
            {
                db.ChampionProfileStats.Add(Profile(championId, magicShare: 0.05, physicalShare: 0.90));
            }
        }

        Seed(db, account, "magic", magicGames, MagicEnemies, MagicResistItem);
        Seed(db, account, "physical", physicalGames, PhysicalEnemies, ArmourItem);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// One profile row per champion. Positions are not seeded per lane: the snapshot falls
    /// back to the champion's best-covered position, which is what the fixture leans on.
    /// </summary>
    private static ChampionProfileStat Profile(int championId, double magicShare, double physicalShare)
        => new()
        {
            ChampionId = championId,
            Position = "MIDDLE",
            Patch = Patch,
            Games = 500,
            Wins = 250,
            GameDurationSecondsSum = 500 * 1800,
            MagicDamageToChampionsSum = (long)(500 * 20_000 * magicShare),
            PhysicalDamageToChampionsSum = (long)(500 * 20_000 * physicalShare),
            TrueDamageToChampionsSum = (long)(500 * 20_000 * (1 - magicShare - physicalShare)),
            ItemGames = 500,
            IsRanged = false,
            AggregatedAtUtc = DateTime.UtcNow,
        };

    private static void Seed(
        TrueMainDbContext db,
        RiotAccount account,
        string prefix,
        int games,
        IReadOnlyList<int> enemies,
        int situationalItemId)
    {
        string[] positions = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

        for (var i = 0; i < games; i++)
        {
            var matchId = $"{prefix}-{i}";
            db.Matches.Add(new MatchBuilder()
                .WithId(matchId).WithQueueId(QueueId).WithGameVersion(Version)
                .WithGameDurationSeconds(1800).WithTimelineIngested().Build());

            // Slot 1 is the tracked main; slots 2-5 its allies, 6-10 the enemy team.
            db.MatchParticipants.Add(Participant(
                matchId, 1, Champion, 100, Position, win: i % 2 == 0,
                riotAccountId: account.Id, puuid: account.Puuid,
                items: [CoreItem, situationalItemId]));

            for (var slot = 1; slot < 5; slot++)
            {
                db.MatchParticipants.Add(Participant(
                    matchId, slot + 1, Allies[slot - 1], 100, positions[slot], win: i % 2 == 0, items: []));
            }

            for (var slot = 0; slot < 5; slot++)
            {
                db.MatchParticipants.Add(Participant(
                    matchId, slot + 6, enemies[slot], 200, positions[slot], win: i % 2 != 0, items: []));
            }

            // Both sides of the played lane get a 15-minute reading, so the in-game axis is
            // evaluable — an even lane, which lands in the middle bucket and is therefore
            // never compared.
            foreach (var participantId in new[] { 1, 6 })
            {
                db.MatchParticipantTimelineSnapshots.Add(new MatchParticipantTimelineSnapshot
                {
                    MatchId = matchId,
                    ParticipantId = participantId,
                    IntervalMinute = 15,
                    TimestampMs = 900_000,
                    TotalGold = 6_000,
                    MinionsKilled = 120,
                    JungleMinionsKilled = 0,
                    Level = 11,
                    Xp = 7_000,
                    Kills = 2,
                    DamageToChampions = 5_000,
                    WardsPlaced = 3,
                    WardsKilled = 1,
                });
            }
        }
    }

    private static MatchParticipant Participant(
        string matchId, int participantId, int championId, int teamId, string position, bool win,
        Guid? riotAccountId = null, string? puuid = null, int[]? items = null)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            Puuid = puuid ?? $"puuid-{matchId}-{participantId}",
            RiotAccountId = riotAccountId,
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
            Item0 = items is { Length: > 0 } ? items[0] : 0,
            Item1 = items is { Length: > 1 } ? items[1] : 0,
            Item6 = 3363,
            TrinketItemId = 3363,
            ItemEvents = [],
            SkillEvents = [],
        };

    private sealed class FakeItemMetadataProvider : IItemMetadataProvider
    {
        private static readonly IReadOnlyDictionary<int, ItemMetadata> Metadata = new Dictionary<int, ItemMetadata>
        {
            [MagicResistItem] = Final(MagicResistItem, "SpellBlock", "Health", "HealthRegen"),
            [ArmourItem] = Final(ArmourItem, "Armor", "Health", "AbilityHaste"),
            [CoreItem] = Final(CoreItem, "Damage", "AbilityHaste"),
        };

        public Task<IReadOnlyDictionary<int, ItemMetadata>> GetItemsAsync(string gameVersion, CancellationToken ct)
            => Task.FromResult(Metadata);

        private static ItemMetadata Final(int id, params string[] categories)
            => new(id, 3000, true, false, false, false, true, false)
            {
                Categories = categories.ToFrozenSet(StringComparer.Ordinal),
            };
    }
}
