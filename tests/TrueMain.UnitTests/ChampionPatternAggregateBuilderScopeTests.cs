using Core.Lol.Patches;
using Data.Entities;
using FluentAssertions;
using Ingestor.Processes.Components.PatternAggregation;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// Locks the Phase 5 data-preservation invariants: whatever the builder
/// emits on the legacy <c>ChampionPatternAggregate</c> wide-table path
/// MUST agree with the new normalised schema on total games / wins, per
/// scope and per dimension. A drift here is the signal that backfill
/// from the old table into the new one would lose rows.
/// </summary>
public sealed class ChampionPatternAggregateBuilderScopeTests
{
    [Fact]
    public async Task DualWrite_preserves_totals_across_legacy_and_normalised_outputs()
    {
        var metadataProvider = Substitute.For<IItemMetadataProvider>();
        metadataProvider
            .GetItemsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<int, ItemMetadata>>(new Dictionary<int, ItemMetadata>
            {
                [1001] = new(1001, 300, true, false, true, true, false, false),
                [3006] = new(3006, 1100, true, false, true, false, true, true),
                [3153] = new(3153, 3200, true, false, false, false, true, false)
            }));
        var builder = new ChampionPatternAggregateBuilder(metadataProvider);

        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var sourceRows = new List<AggregateSourceRow>
        {
            BuildRow(accountA, championId: 22, win: true, summoner1: 4, summoner2: 7, skillBias: 1),
            BuildRow(accountA, championId: 22, win: false, summoner1: 4, summoner2: 7, skillBias: 1),
            BuildRow(accountA, championId: 22, win: true, summoner1: 4, summoner2: 11, skillBias: 2),
            BuildRow(accountB, championId: 22, win: true, summoner1: 4, summoner2: 11, skillBias: 2)
        };

        var result = await builder.BuildAggregatesAsync(
            sourceRows,
            DateTime.UtcNow,
            CancellationToken.None);

        result.AggregateRows.Sum(a => a.Games).Should().Be(sourceRows.Count);
        result.AggregateRows.Sum(a => a.Wins).Should().Be(sourceRows.Count(row => row.Win));

        result.Scopes.Sum(s => s.Games).Should().Be(sourceRows.Count);
        result.Scopes.Sum(s => s.Wins).Should().Be(sourceRows.Count(row => row.Win));

        foreach (var scope in result.Scopes)
        {
            scope.SpellPairs.Sum(p => p.Games).Should().Be(
                scope.Games,
                because: "the spell-pair dimension must sum to the scope total");
            scope.SpellPairs.Sum(p => p.Wins).Should().Be(scope.Wins);

            scope.SkillOrders.Sum(o => o.Games).Should().Be(scope.Games);
            scope.SkillOrders.Sum(o => o.Wins).Should().Be(scope.Wins);

            scope.StarterItems.Sum(s => s.Games).Should().Be(scope.Games);
            scope.StarterItems.Sum(s => s.Wins).Should().Be(scope.Wins);

            scope.Builds.Sum(b => b.Games).Should().Be(scope.Games);
            scope.Builds.Sum(b => b.Wins).Should().Be(scope.Wins);
        }
    }

    [Fact]
    public async Task DualWrite_produces_one_scope_per_account_champion_patch_platform_queue_position()
    {
        var metadataProvider = Substitute.For<IItemMetadataProvider>();
        metadataProvider
            .GetItemsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<int, ItemMetadata>>(new Dictionary<int, ItemMetadata>()));
        var builder = new ChampionPatternAggregateBuilder(metadataProvider);

        var account = Guid.NewGuid();
        // Two distinct positions, three distinct patches, one champion / one platform / one queue.
        // => 2 * 3 = 6 expected scopes.
        var sourceRows = new List<AggregateSourceRow>();
        foreach (var position in new[] { "BOTTOM", "UTILITY" })
        {
            foreach (var patch in new[] { "16.4", "16.5", "16.6" })
            {
                sourceRows.Add(BuildRow(account, championId: 22, win: true, summoner1: 4, summoner2: 7, skillBias: 1, position: position, patch: patch));
            }
        }

        var result = await builder.BuildAggregatesAsync(
            sourceRows,
            DateTime.UtcNow,
            CancellationToken.None);

        result.Scopes.Should().HaveCount(6);
        result.Scopes
            .Select(s => (s.RiotAccountId, s.ChampionId, s.GameVersion, s.PlatformId, s.QueueId, s.Position))
            .Distinct()
            .Should().HaveCount(6);
    }

    private static AggregateSourceRow BuildRow(
        Guid accountId,
        int championId,
        bool win,
        int summoner1,
        int summoner2,
        int skillBias,
        string position = "BOTTOM",
        string patch = "16.4")
    {
        return new AggregateSourceRow
        {
            MatchId = Guid.NewGuid().ToString("N"),
            RiotAccountId = accountId,
            ChampionId = championId,
            GameVersion = PatchVersion.Normalize(patch),
            PlatformId = "KR",
            QueueId = 420,
            GameStartTimeUtc = DateTime.UtcNow.AddHours(-1),
            GameDurationSeconds = 1_800,
            Win = win,
            Position = position,
            Summoner1Id = summoner1,
            Summoner2Id = summoner2,
            PrimaryStyleId = 8100,
            SubStyleId = 8300,
            PerksOffense = 5005,
            PerksFlex = 5008,
            PerksDefense = 5002,
            Item0 = 3153, Item1 = 3006, Item2 = 0, Item3 = 0, Item4 = 0, Item5 = 0, Item6 = 3340,
            ItemEvents =
            [
                new ItemEvent { TimestampMs = 5_000 * skillBias, ItemId = 1001, EventType = "ITEM_PURCHASED" },
                new ItemEvent { TimestampMs = 300_000, ItemId = 3006, EventType = "ITEM_PURCHASED" },
                new ItemEvent { TimestampMs = 600_000, ItemId = 3153, EventType = "ITEM_PURCHASED" }
            ],
            SkillEvents =
            [
                new SkillEvent { TimestampMs = 1_000, SkillSlot = skillBias, LevelUpType = "NORMAL" },
                new SkillEvent { TimestampMs = 2_000, SkillSlot = skillBias, LevelUpType = "NORMAL" },
                new SkillEvent { TimestampMs = 3_000, SkillSlot = (skillBias % 3) + 1, LevelUpType = "NORMAL" },
                new SkillEvent { TimestampMs = 4_000, SkillSlot = (skillBias % 3) + 1, LevelUpType = "NORMAL" },
                new SkillEvent { TimestampMs = 5_000, SkillSlot = ((skillBias + 1) % 3) + 1, LevelUpType = "NORMAL" },
                new SkillEvent { TimestampMs = 6_000, SkillSlot = ((skillBias + 1) % 3) + 1, LevelUpType = "NORMAL" }
            ]
        };
    }
}
