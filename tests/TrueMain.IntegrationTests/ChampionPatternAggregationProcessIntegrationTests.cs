using Core.Lol.Map;
using Core.Options;
using Data;
using Data.Entities;
using FluentAssertions;
using Ingestor.Processes;
using Ingestor.Processes.Components.PatternAggregation;
using Ingestor.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.IntegrationTests;

public sealed class ChampionPatternAggregationProcessIntegrationTests : IClassFixture<PostgresFixture>
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
        await process.RunAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var aggregates = await verifyDb.ChampionPatternAggregates
            .OrderBy(a => a.GameVersion)
            .ToListAsync();

        aggregates.Should().HaveCount(2);

        var aggregate = aggregates.Single(a =>
            a.GameVersion == "16.5"
            && a.BuildItem0 == 3153
            && a.BuildItem1 == 6672
            && a.BuildItem2 == 0);
        aggregate.RiotAccountId.Should().Be(_riotAccountId);
        aggregate.ChampionId.Should().Be(22);
        aggregate.PlatformId.Should().Be("KR");
        aggregate.Position.Should().Be("BOTTOM");
        aggregate.StarterItems.Should().Equal(1055, 2003);
        aggregate.BootsItemId.Should().Be(3006);
        aggregate.BuildItem0.Should().Be(3153);
        aggregate.BuildItem1.Should().Be(6672);
        aggregate.BuildItem2.Should().Be(0);
        aggregate.BuildItem3.Should().Be(0);
        aggregate.BuildItem6.Should().Be(0);
        aggregate.Games.Should().Be(1);
        aggregate.Wins.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_ShouldReplaceExistingRowsDeterministically()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedChampionPatternDataAsync();

        var process = CreateProcess();
        await process.RunAsync(CancellationToken.None);
        await process.RunAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var aggregates = await verifyDb.ChampionPatternAggregates.ToListAsync();

        aggregates.Should().HaveCount(2);
        aggregates.Should().OnlyHaveUniqueItems(a =>
            $"{a.RiotAccountId}:{a.GameVersion}:{string.Join("-", a.StarterItems)}:{a.BuildItem0}:{a.BuildItem1}:{a.BuildItem2}:{a.BuildItem3}:{a.BuildItem4}:{a.BuildItem5}:{a.BuildItem6}");
    }

    [Fact]
    public async Task RunAsync_ShouldPurgeAggregatesWhenAllSupportingStatsBecomeUnqualified()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedChampionPatternDataAsync();

        var process = CreateProcess();
        await process.RunAsync(CancellationToken.None);

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

        await process.RunAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        (await verifyDb.ChampionPatternAggregates.ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ShouldIgnoreMatchesShorterThanFifteenMinutes()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedChampionPatternDataAsync(includeShortMatch: true);

        var process = CreateProcess();
        await process.RunAsync(CancellationToken.None);

        await using var verifyDb = _fixture.CreateDbContext();
        var aggregates = await verifyDb.ChampionPatternAggregates.ToListAsync();

        aggregates.Should().HaveCount(2);
        aggregates.Should().NotContain(aggregate => aggregate.GameVersion == "16.6");
    }

    private ChampionPatternAggregationProcess CreateProcess()
        => new(
            NullLogger<ChampionPatternAggregationProcess>.Instance,
            new FakeProcessRunRecorder(),
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions { QueueId = LolQueueIds.RankedSoloDuo }),
            new ChampionPatternSourceRowReader(new TestDbContextFactory(_fixture)),
            new ChampionPatternAggregateBuilder(
                new FakeItemMetadataProvider()),
            new ChampionPatternAggregatePersister(new TestDbContextFactory(_fixture)));

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
                QueueId = LolQueueIds.RankedSoloDuo,
                MapId = LolMapIds.SummonersRift,
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
                QueueId = LolQueueIds.RankedSoloDuo,
                MapId = LolMapIds.SummonersRift,
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
                QueueId = LolQueueIds.RankedSoloDuo,
                MapId = LolMapIds.SummonersRift,
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

    private sealed class TestDbContextFactory(PostgresFixture fixture) : IDbContextFactory<TrueMainDbContext>
    {
        public TrueMainDbContext CreateDbContext() => fixture.CreateDbContext();

        public Task<TrueMainDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(fixture.CreateDbContext());
    }

    private sealed class FakeProcessRunRecorder : IProcessRunRecorder
    {
        public Task RecordAsync(
            string processName,
            DateTime startedAtUtc,
            DateTime completedAtUtc,
            ProcessRunStatus status,
            object? metrics,
            string? error,
            CancellationToken ct)
            => Task.CompletedTask;
    }
}
