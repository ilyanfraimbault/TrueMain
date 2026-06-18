using Core.Lol.Map;
using Core.Options;
using Data.Entities;
using AwesomeAssertions;
using Ingestor.Processes;
using Ingestor.Processes.Components.PatternAggregation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ChampionPatternAggregationProcessIntegrationTests
{
    private readonly PostgresFixture _fixture;
    private readonly Guid _riotAccountId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public ChampionPatternAggregationProcessIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunAsync_ShouldPersistAccountScopedChampionPatternAggregates()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedChampionPatternDataAsync();

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        // Both seeded matches (16.5.2, 16.5.1) normalise to patch "16.5",
        // same account / champion / position / platform / queue → one scope
        // with two patterns (one per distinct build combo).
        var scope = await verifyDb.ChampionAggregateScopes.SingleAsync();
        scope.RiotAccountId.Should().Be(_riotAccountId);
        scope.ChampionId.Should().Be(22);
        scope.PlatformId.Should().Be("KR");
        scope.Position.Should().Be("BOTTOM");
        scope.GameVersion.Should().Be("16.5");
        scope.Games.Should().Be(2);
        scope.Wins.Should().Be(1);

        // Match the seeded combo (build items [3153, 6672, 0...], boots 3006,
        // starter [1055, 2003]) through the dim tables to find its pattern.
        var combo = await (
            from pattern in verifyDb.ChampionAggregatePatterns
            join build in verifyDb.ChampionDimBuilds on pattern.BuildId equals build.Id
            join starter in verifyDb.ChampionDimStarterItems on pattern.StarterItemsId equals starter.Id
            where pattern.ScopeId == scope.Id
                && build.BuildItem0 == 3153
                && build.BuildItem1 == 6672
                && build.BuildItem2 == 0
            select new { pattern.Games, pattern.Wins, build.BootsItemId, starter.StarterItems })
            .SingleAsync();

        combo.BootsItemId.Should().Be(3006);
        combo.StarterItems.Should().Equal(1055, 2003);
        combo.Games.Should().Be(1);
        combo.Wins.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_ShouldReplaceExistingRowsDeterministically()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedChampionPatternDataAsync();

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);
        await process.RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var scopes = await verifyDb.ChampionAggregateScopes.AsNoTracking().ToListAsync();
        var patterns = await verifyDb.ChampionAggregatePatterns.AsNoTracking().ToListAsync();

        scopes.Should().ContainSingle();
        patterns.Should().HaveCount(2,
            "two source matches with distinct builds = two pattern rows in the same scope; "
            + "the second run deletes + reinserts via the cascade so we must not see duplicates");
    }

    [Fact]
    public async Task RunAsync_ShouldPurgeAggregatesWhenAllSupportingStatsBecomeUnqualified()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedChampionPatternDataAsync();

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);

        await using (var mutateDb = _fixture.CreateDbContext())
        {
            var stats = await mutateDb.MainChampionStats.ToListAsync();
            foreach (var stat in stats)
            {
                stat.IsMain = false;
                stat.IsOtp = false;
            }

            await mutateDb.SaveChangesAsync();
        }

        await process.RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        (await verifyDb.ChampionAggregateScopes.ToListAsync()).Should().BeEmpty();
        (await verifyDb.ChampionAggregatePatterns.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_PopulatesGloballyDeduplicatedDimensionRows()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedChampionPatternDataAsync();

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var dimBuilds = await verifyDb.ChampionDimBuilds.AsNoTracking().ToListAsync();
        var dimRunes = await verifyDb.ChampionDimRunePages.AsNoTracking().ToListAsync();
        var dimSpellPairs = await verifyDb.ChampionDimSpellPairs.AsNoTracking().ToListAsync();
        var dimSkillOrders = await verifyDb.ChampionDimSkillOrders.AsNoTracking().ToListAsync();
        var dimStarters = await verifyDb.ChampionDimStarterItems.AsNoTracking().ToListAsync();

        dimBuilds.Should().NotBeEmpty();
        dimRunes.Should().NotBeEmpty();
        dimSpellPairs.Should().NotBeEmpty();
        dimSkillOrders.Should().NotBeEmpty();
        dimStarters.Should().NotBeEmpty();

        // Re-running must NOT add duplicate dim rows (get-or-create idempotency).
        await process.RunCoreAsync(CancellationToken.None);

        await using var rerunDb = _fixture.CreateDbContext();
        (await rerunDb.ChampionDimBuilds.CountAsync()).Should().Be(dimBuilds.Count);
        (await rerunDb.ChampionDimRunePages.CountAsync()).Should().Be(dimRunes.Count);
        (await rerunDb.ChampionDimSpellPairs.CountAsync()).Should().Be(dimSpellPairs.Count);
        (await rerunDb.ChampionDimSkillOrders.CountAsync()).Should().Be(dimSkillOrders.Count);
        (await rerunDb.ChampionDimStarterItems.CountAsync()).Should().Be(dimStarters.Count);
    }

    [Fact]
    public async Task RunAsync_ShouldKeepAggregatesForPatchesWhoseMatchesWerePurgedByRetention()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedChampionPatternDataAsync();

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);

        // Simulate MatchDataRetention: the 16.5 matches/participants fall out of
        // the retention window and are purged, while a fresh 16.6 patch arrives.
        await using (var mutateDb = _fixture.CreateDbContext())
        {
            mutateDb.MatchParticipants.RemoveRange(await mutateDb.MatchParticipants.ToListAsync());
            mutateDb.Matches.RemoveRange(await mutateDb.Matches.ToListAsync());
            await mutateDb.SaveChangesAsync();
        }

        await SeedSecondPatchDataAsync();
        await process.RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var scopes = await verifyDb.ChampionAggregateScopes.AsNoTracking().ToListAsync();

        // The 16.5 scope is frozen (its matches are gone so it can't be rebuilt),
        // and the live 16.6 patch produces its own scope. Neither is wiped.
        scopes.Should().HaveCount(2);
        scopes.Select(scope => scope.GameVersion).Should().BeEquivalentTo(["16.5", "16.6"]);

        var frozen = scopes.Single(scope => scope.GameVersion == "16.5");
        frozen.Games.Should().Be(2);
        frozen.Wins.Should().Be(1);

        var live = scopes.Single(scope => scope.GameVersion == "16.6");
        live.Games.Should().Be(1);
        live.Wins.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_ShouldIgnoreMatchesShorterThanFifteenMinutes()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedChampionPatternDataAsync(includeShortMatch: true);

        var process = CreateProcess();
        await process.RunCoreAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var scopes = await verifyDb.ChampionAggregateScopes.ToListAsync();

        // Both 16.5.x matches collapse to one "16.5" scope; the 16.6 match
        // is filtered out as too short, so there's no second scope to keep.
        scopes.Should().ContainSingle();
        scopes.Should().NotContain(scope => scope.GameVersion == "16.6");
    }

    private ChampionPatternAggregationProcess CreateProcess()
    {
        var dbContextFactory = new TestDbContextFactory(_fixture);
        return new ChampionPatternAggregationProcess(
            NullLogger<ChampionPatternAggregationProcess>.Instance,
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions { QueueId = LolQueueId.RankedSoloDuo }),
            new ChampionPatternSourceRowReader(dbContextFactory),
            new ChampionPatternAggregateBuilder(new FakeItemMetadataProvider()),
            new ChampionPatternAggregatePersister(
                dbContextFactory,
                new ChampionDimensionResolver(dbContextFactory)),
            TimeProvider.System);
    }

    private async Task SeedChampionPatternDataAsync(bool includeShortMatch = false)
    {
        var now = DateTime.UtcNow;

        await using var db = _fixture.CreateDbContext();

        db.RiotAccounts.Add(new RiotAccount
        {
            Id = _riotAccountId,
            PlatformId = "KR",
            Puuid = "puuid-1",
            GameName = "main-one",
            SummonerId = "summoner-1",
            ProfileIconId = 1,
            SummonerLevel = 100,
            LastProfileSyncAtUtc = now,
            CreatedAtUtc = now.AddDays(-10),
            UpdatedAtUtc = now.AddDays(-1)
        });

        db.MainChampionStats.Add(new MainChampionStat
        {
            PlatformId = "KR",
            Puuid = "puuid-1",
            ChampionId = 22,
            TotalMatches = 10,
            ChampionMatches = 9,
            PlayRate = 0.9,
            IsMain = true,
            IsOtp = true,
            PrimaryPosition = "BOTTOM",
            CalculatedAtUtc = now.AddMinutes(-5)
        });

        db.Matches.AddRange(
            new Match
            {
                Id = "KR_AGG_1",
                PlatformId = "KR",
                QueueId = (int)LolQueueId.RankedSoloDuo,
                MapId = (int)LolMapId.SummonersRift,
                GameMode = "CLASSIC",
                GameType = "MATCHED_GAME",
                GameStartTimeUtc = now.AddHours(-2),
                GameDurationSeconds = 1800,
                GameVersion = "16.5.2",
                CreatedAtUtc = now.AddHours(-2),
                TimelineIngested = true
            },
            new Match
            {
                Id = "KR_AGG_2",
                PlatformId = "KR",
                QueueId = (int)LolQueueId.RankedSoloDuo,
                MapId = (int)LolMapId.SummonersRift,
                GameMode = "CLASSIC",
                GameType = "MATCHED_GAME",
                GameStartTimeUtc = now.AddHours(-1),
                GameDurationSeconds = 1800,
                GameVersion = "16.5.1",
                CreatedAtUtc = now.AddHours(-1),
                TimelineIngested = true
            });

        if (includeShortMatch)
        {
            db.Matches.Add(new Match
            {
                Id = "KR_AGG_3",
                PlatformId = "KR",
                QueueId = (int)LolQueueId.RankedSoloDuo,
                MapId = (int)LolMapId.SummonersRift,
                GameMode = "CLASSIC",
                GameType = "MATCHED_GAME",
                GameStartTimeUtc = now.AddMinutes(-30),
                GameDurationSeconds = 14 * 60,
                GameVersion = "16.6.1",
                CreatedAtUtc = now.AddMinutes(-30),
                TimelineIngested = true
            });
        }

        db.MatchParticipants.AddRange(
            BuildParticipant(Guid.Parse("11111111-1111-1111-1111-111111111111"), "KR_AGG_1", true, [3153, 3006, 6672]),
            BuildParticipant(Guid.Parse("22222222-2222-2222-2222-222222222222"), "KR_AGG_2", false, [3006, 3031, 3085]));

        if (includeShortMatch)
        {
            db.MatchParticipants.Add(BuildParticipant(
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "KR_AGG_3",
                true,
                [6672, 3006, 3031]));
        }

        await db.SaveChangesAsync();
    }

    private async Task SeedSecondPatchDataAsync()
    {
        var now = DateTime.UtcNow;

        await using var db = _fixture.CreateDbContext();

        db.Matches.Add(new Match
        {
            Id = "KR_AGG_16_6",
            PlatformId = "KR",
            QueueId = (int)LolQueueId.RankedSoloDuo,
            MapId = (int)LolMapId.SummonersRift,
            GameMode = "CLASSIC",
            GameType = "MATCHED_GAME",
            GameStartTimeUtc = now.AddMinutes(-10),
            GameDurationSeconds = 1800,
            GameVersion = "16.6.1",
            CreatedAtUtc = now.AddMinutes(-10),
            TimelineIngested = true
        });

        db.MatchParticipants.Add(BuildParticipant(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "KR_AGG_16_6",
            true,
            [3153, 3006, 6672]));

        await db.SaveChangesAsync();
    }

    private MatchParticipant BuildParticipant(Guid id, string matchId, bool win, IReadOnlyList<int> finalItems)
    {
        return new MatchParticipant
        {
            Id = id,
            MatchId = matchId,
            ParticipantId = 1,
            Puuid = "puuid-1",
            RiotAccountId = _riotAccountId,
            SummonerName = "main-one",
            SummonerLevel = 100,
            ChampionId = 22,
            TeamId = 100,
            TeamPosition = "BOTTOM",
            IndividualPosition = "BOTTOM",
            Lane = "BOTTOM",
            Role = "DUO_CARRY",
            Win = win,
            Kills = 1,
            Deaths = 1,
            Assists = 1,
            GoldEarned = 8000,
            TotalMinionsKilled = 100,
            NeutralMinionsKilled = 0,
            ChampLevel = 12,
            Item0 = finalItems.ElementAtOrDefault(0),
            Item1 = finalItems.ElementAtOrDefault(1),
            Item2 = finalItems.ElementAtOrDefault(2),
            Item3 = finalItems.ElementAtOrDefault(3),
            Item4 = finalItems.ElementAtOrDefault(4),
            Item5 = finalItems.ElementAtOrDefault(5),
            Item6 = 3363,
            TrinketItemId = 3363,
            PerksDefense = 5001,
            PerksFlex = 5008,
            PerksOffense = 5005,
            PrimaryStyleId = 8000,
            SubStyleId = 8100,
            Summoner1Id = 4,
            Summoner2Id = 7,
            ItemEvents =
            [
                new ItemEvent { TimestampMs = 1000, ItemId = 1055, EventType = "ITEM_PURCHASED" },
                new ItemEvent { TimestampMs = 1500, ItemId = 2003, EventType = "ITEM_PURCHASED" },
                new ItemEvent { TimestampMs = 120000, ItemId = 3006, EventType = "ITEM_PURCHASED" },
                new ItemEvent { TimestampMs = 240000, ItemId = finalItems[0], EventType = "ITEM_PURCHASED" },
                new ItemEvent { TimestampMs = 360000, ItemId = finalItems[1], EventType = "ITEM_PURCHASED" }
            ],
            SkillEvents =
            [
                new SkillEvent { TimestampMs = 1000, SkillSlot = 1, LevelUpType = "NORMAL" },
                new SkillEvent { TimestampMs = 2000, SkillSlot = 2, LevelUpType = "NORMAL" },
                new SkillEvent { TimestampMs = 3000, SkillSlot = 3, LevelUpType = "NORMAL" }
            ]
        };
    }

    private sealed class FakeItemMetadataProvider : IItemMetadataProvider
    {
        private static readonly IReadOnlyDictionary<int, ItemMetadata> Metadata = new Dictionary<int, ItemMetadata>
        {
            [2003] = new(2003, 50, true, true, false, false, true, false),
            [1055] = new(1055, 450, true, false, false, false, false, false),
            [3006] = new(3006, 1100, true, false, true, false, true, true),
            [3031] = new(3031, 3000, true, false, false, false, true, false),
            [3085] = new(3085, 3000, true, false, false, false, true, false),
            [3153] = new(3153, 3200, true, false, false, false, true, false),
            [6672] = new(6672, 3000, true, false, false, false, true, false)
        };

        public Task<IReadOnlyDictionary<int, ItemMetadata>> GetItemsAsync(string gameVersion, CancellationToken ct)
            => Task.FromResult(Metadata);
    }

}
