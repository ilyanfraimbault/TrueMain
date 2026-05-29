using Core.Lol.Patches;
using Data.Entities;
using AwesomeAssertions;
using Ingestor.Processes.Components.PatternAggregation;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// Locks the Phase 6 builder invariants: scopes carry the right
/// per-(account, champion, patch, platform, queue, position) totals, and
/// pattern intents preserve every observation as a (full combo) tuple.
/// Drift here means the persister would write inconsistent counts.
/// </summary>
public sealed class ChampionPatternAggregateBuilderScopeTests
{
    [Fact]
    public async Task BuildAggregates_PreservesTotalsAcrossScopesAndPatterns()
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

        result.Scopes.Sum(s => s.Games).Should().Be(sourceRows.Count);
        result.Scopes.Sum(s => s.Wins).Should().Be(sourceRows.Count(row => row.Win));

        // Each pattern intent represents one (scope + full combo) — summing
        // games across patterns must equal the scope totals (no observation
        // double-counted, none lost).
        result.Patterns.Sum(p => p.Games).Should().Be(sourceRows.Count);
        result.Patterns.Sum(p => p.Wins).Should().Be(sourceRows.Count(row => row.Win));

        foreach (var scope in result.Scopes)
        {
            var scopePatterns = result.Patterns.Where(p => p.ScopeId == scope.Id).ToList();
            scopePatterns.Sum(p => p.Games).Should().Be(scope.Games);
            scopePatterns.Sum(p => p.Wins).Should().Be(scope.Wins);
        }
    }

    [Fact]
    public async Task BuildAggregates_FoldsIdenticalCombosIntoSinglePattern()
    {
        var metadataProvider = Substitute.For<IItemMetadataProvider>();
        metadataProvider
            .GetItemsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<int, ItemMetadata>>(new Dictionary<int, ItemMetadata>()));
        var builder = new ChampionPatternAggregateBuilder(metadataProvider);

        var account = Guid.NewGuid();
        var sharedPage = new PageSpec(
            PrimaryStyleId: 8000, PrimaryKeystoneId: 8005,
            PrimaryPerk1Id: 9111, PrimaryPerk2Id: 9104, PrimaryPerk3Id: 8014,
            SecondaryStyleId: 8100, SecondaryPerk1Id: 8139, SecondaryPerk2Id: 8135,
            StatOffense: 5005, StatFlex: 5008, StatDefense: 5002);
        var otherPage = sharedPage with { PrimaryKeystoneId = 8021 }; // different keystone

        var sourceRows = new List<AggregateSourceRow>
        {
            BuildRow(account, championId: 22, win: true, summoner1: 4, summoner2: 7, skillBias: 1, page: sharedPage),
            BuildRow(account, championId: 22, win: false, summoner1: 4, summoner2: 7, skillBias: 1, page: sharedPage),
            BuildRow(account, championId: 22, win: true, summoner1: 4, summoner2: 7, skillBias: 1, page: otherPage)
        };

        var result = await builder.BuildAggregatesAsync(sourceRows, DateTime.UtcNow, CancellationToken.None);

        result.Scopes.Should().ContainSingle();
        result.Patterns.Should().HaveCount(2,
            "two distinct rune pages with the same other dims = two patterns");
        result.Patterns.Single(p => p.RunePage.PrimaryKeystoneId == 8005).Games.Should().Be(2);
        result.Patterns.Single(p => p.RunePage.PrimaryKeystoneId == 8005).Wins.Should().Be(1);
        result.Patterns.Single(p => p.RunePage.PrimaryKeystoneId == 8021).Games.Should().Be(1);
    }

    private readonly record struct PageSpec(
        int PrimaryStyleId,
        int PrimaryKeystoneId,
        int PrimaryPerk1Id,
        int PrimaryPerk2Id,
        int PrimaryPerk3Id,
        int SecondaryStyleId,
        int SecondaryPerk1Id,
        int SecondaryPerk2Id,
        int StatOffense,
        int StatFlex,
        int StatDefense);

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
        string patch = "16.4",
        PageSpec? page = null)
    {
        var runePage = page ?? new PageSpec(
            PrimaryStyleId: 8100, PrimaryKeystoneId: 0,
            PrimaryPerk1Id: 0, PrimaryPerk2Id: 0, PrimaryPerk3Id: 0,
            SecondaryStyleId: 8300, SecondaryPerk1Id: 0, SecondaryPerk2Id: 0,
            StatOffense: 5005, StatFlex: 5008, StatDefense: 5002);

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
            PrimaryStyleId = runePage.PrimaryStyleId,
            SubStyleId = runePage.SecondaryStyleId,
            PerksOffense = runePage.StatOffense,
            PerksFlex = runePage.StatFlex,
            PerksDefense = runePage.StatDefense,
            PrimaryKeystoneId = runePage.PrimaryKeystoneId,
            PrimaryPerk1Id = runePage.PrimaryPerk1Id,
            PrimaryPerk2Id = runePage.PrimaryPerk2Id,
            PrimaryPerk3Id = runePage.PrimaryPerk3Id,
            SecondaryPerk1Id = runePage.SecondaryPerk1Id,
            SecondaryPerk2Id = runePage.SecondaryPerk2Id,
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
