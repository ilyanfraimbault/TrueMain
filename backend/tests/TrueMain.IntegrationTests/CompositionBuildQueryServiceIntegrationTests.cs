using AwesomeAssertions;
using Data.BuildFacts;
using Data.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Service-level coverage of the composition build aggregation (#563): the
/// top-K refs are folded into one coherent recommendation (runes, starter,
/// boots, core path, situational items, spells, skill order), and sparse
/// games — no timeline, no rune selections, no item metadata — abstain per
/// dimension instead of failing the whole recommendation.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class CompositionBuildQueryServiceIntegrationTests
{
    private const int Champion = 157; // Yone
    private const string Position = "MIDDLE";
    private const string KnownVersion = "16.4.521.123";
    private const string UnknownVersion = "15.1.100.1"; // aged out of CDragon

    private readonly PostgresFixture _fixture;

    public CompositionBuildQueryServiceIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AggregateAsync_FoldsTopKIntoACoherentRecommendation()
    {
        await _fixture.ResetDatabaseAsync();

        // Two wins on the same build (Hubris → Spear of Shojin) and one loss
        // on a different one — the win-weighted vote must elect the win build.
        await SeedGameAsync("COMPB_WIN1", win: true, buildOrder: [3031, 3153], withRunes: true);
        await SeedGameAsync("COMPB_WIN2", win: true, buildOrder: [3031, 3153], withRunes: true);
        await SeedGameAsync("COMPB_LOSS", win: false, buildOrder: [3072, 3026], withRunes: true);

        var result = await CreateService().AggregateAsync(
            Champion,
            Position,
            [Ref("COMPB_WIN1", true), Ref("COMPB_WIN2", true), Ref("COMPB_LOSS", false)],
            CancellationToken.None);

        result.GamesConsidered.Should().Be(3);
        result.Wins.Should().Be(2);

        result.CorePath.Should().NotBeNull();
        result.CorePath!.ItemIds.Should().Equal(3031, 3153);
        result.CorePath.Games.Should().Be(2);
        result.CorePath.WinRate.Should().Be(1d);

        result.Boots.Should().NotBeNull();
        result.Boots!.ItemIds.Should().Equal(3006);

        result.StarterItems.Should().NotBeNull();
        result.StarterItems!.ItemIds.Should().Contain(1055);

        result.SituationalItems.Should().NotBeEmpty();
        result.SituationalItems.SelectMany(s => s.ItemIds).Should().Contain(3072)
            .And.NotContain(3031, "core-path items are not situational");

        result.SummonerSpells.Should().NotBeNull();
        result.SummonerSpells!.Spell1Id.Should().Be(4);
        result.SummonerSpells.Spell2Id.Should().Be(12);
        result.SummonerSpells.Games.Should().Be(3);

        result.SkillOrder.Should().NotBeNull();
        result.SkillOrder!.Sequence.Should().Equal("Q", "W", "E");

        result.RunePage.Should().NotBeNull();
        result.RunePage!.PrimaryStyleId.Should().Be(8000);
        result.RunePage.PrimaryKeystoneId.Should().Be(8010);
        result.RunePage.PrimaryPerk3Id.Should().Be(8014);
        result.RunePage.SecondaryStyleId.Should().Be(8100);
        result.RunePage.SecondaryPerk2Id.Should().Be(8135);
        result.RunePage.StatOffense.Should().Be(5005);
        result.RunePage.Games.Should().Be(3);
    }

    [Fact]
    public async Task AggregateAsync_SparseGame_AbstainsPerDimensionWithoutThrowing()
    {
        await _fixture.ResetDatabaseAsync();

        // No timeline events, no rune selections, and a game version the
        // metadata source no longer serves: only the spell vote survives.
        await SeedGameAsync(
            "COMPB_SPARSE",
            win: true,
            buildOrder: [],
            withRunes: false,
            withSkillEvents: false,
            gameVersion: UnknownVersion);

        var result = await CreateService().AggregateAsync(
            Champion, Position, [Ref("COMPB_SPARSE", true)], CancellationToken.None);

        result.GamesConsidered.Should().Be(1);
        result.Wins.Should().Be(1);
        result.CorePath.Should().BeNull();
        result.Boots.Should().BeNull();
        result.StarterItems.Should().BeNull();
        result.SituationalItems.Should().BeEmpty();
        result.RunePage.Should().BeNull();
        result.SkillOrder.Should().BeNull();
        result.SummonerSpells.Should().NotBeNull("spells live on the participant row itself");
    }

    [Fact]
    public async Task AggregateAsync_EmptyTopK_ReturnsEmptyRecommendation()
    {
        await _fixture.ResetDatabaseAsync();

        var result = await CreateService().AggregateAsync(
            Champion, Position, [], CancellationToken.None);

        result.GamesConsidered.Should().Be(0);
        result.SituationalItems.Should().BeEmpty();
    }

    private CompositionBuildQueryService CreateService()
        => new(
            _fixture.CreateDbContext(),
            new FakeItemMetadataProvider(),
            Microsoft.Extensions.Options.Options.Create(new CompositionSearchOptions()),
            NullLogger<CompositionBuildQueryService>.Instance);

    private static CompositionMatchRef Ref(string matchId, bool win)
        => new()
        {
            MatchId = matchId,
            ParticipantId = 1,
            Score = 10,
            Win = win,
            GameStartTimeUtc = DateTime.UtcNow.AddDays(-1),
        };

    /// <summary>
    /// Seeds one game with only the searched participant (the service loads
    /// nothing else): starter purchase inside the 120s window, boots, then the
    /// requested legendaries in order, plus a Q→W→E skill sequence and the
    /// Conqueror-style rune page when requested.
    /// </summary>
    private async Task SeedGameAsync(
        string matchId,
        bool win,
        int[] buildOrder,
        bool withRunes,
        bool withSkillEvents = true,
        string gameVersion = KnownVersion)
    {
        await using var db = _fixture.CreateDbContext();

        db.Matches.Add(new MatchBuilder()
            .WithId(matchId)
            .WithGameVersion(gameVersion)
            .WithGameStartTimeUtc(DateTime.UtcNow.AddDays(-1))
            .Build());

        var itemEvents = new List<ItemEvent>();
        if (buildOrder.Length > 0)
        {
            itemEvents.Add(Purchase(10_000, 1055));
            itemEvents.Add(Purchase(600_000, 3006));
            for (var i = 0; i < buildOrder.Length; i++)
            {
                itemEvents.Add(Purchase(700_000 + i * 100_000, buildOrder[i]));
            }
        }

        var skillEvents = withSkillEvents
            ? new List<SkillEvent>
            {
                Skill(60_000, 1),
                Skill(120_000, 2),
                Skill(180_000, 1),
                Skill(240_000, 2),
                Skill(300_000, 3),
                Skill(360_000, 3),
            }
            : [];

        var finalItems = new int[7];
        finalItems[0] = buildOrder.Length > 0 ? 1055 : 0;
        finalItems[1] = buildOrder.Length > 0 ? 3006 : 0;
        for (var i = 0; i < buildOrder.Length && i < 5; i++)
        {
            finalItems[2 + i] = buildOrder[i];
        }

        db.MatchParticipants.Add(new MatchParticipant
        {
            MatchId = matchId,
            ParticipantId = 1,
            Puuid = $"puuid-{matchId}-1",
            RiotAccountId = null,
            SummonerName = "seed",
            SummonerLevel = 100,
            ChampionId = Champion,
            TeamId = 100,
            TeamPosition = Position,
            IndividualPosition = Position,
            Lane = Position,
            Role = "SOLO",
            Win = win,
            Kills = 5,
            Deaths = 4,
            Assists = 6,
            GoldEarned = 12000,
            TotalMinionsKilled = 180,
            NeutralMinionsKilled = 4,
            ChampLevel = 16,
            Item0 = finalItems[0],
            Item1 = finalItems[1],
            Item2 = finalItems[2],
            Item3 = finalItems[3],
            Item4 = finalItems[4],
            Item5 = finalItems[5],
            Item6 = finalItems[6],
            TrinketItemId = 3363,
            PerksDefense = 5001,
            PerksFlex = 5008,
            PerksOffense = 5005,
            PrimaryStyleId = 8000,
            SubStyleId = 8100,
            Summoner1Id = 12, // canonicalised to (4, 12) by the service
            Summoner2Id = 4,
            EloBracket = "",
            ItemEvents = itemEvents,
            SkillEvents = skillEvents,
        });

        await db.SaveChangesAsync();

        if (withRunes)
        {
            await SeedRunePageAsync(db, matchId);
        }
    }

    private static async Task SeedRunePageAsync(Data.TrueMainDbContext db, string matchId)
    {
        (string Style, int Index, int PerkId)[] selections =
        [
            ("primaryStyle", 0, 8010),
            ("primaryStyle", 1, 9111),
            ("primaryStyle", 2, 9104),
            ("primaryStyle", 3, 8014),
            ("subStyle", 0, 8139),
            ("subStyle", 1, 8135),
        ];

        foreach (var (style, index, perkId) in selections)
        {
            var styleId = style == "primaryStyle" ? 8000 : 8100;
            var catalog = db.PerkSelectionCatalogs.Local
                .FirstOrDefault(c =>
                    c.StyleId == styleId && c.SelectionIndex == index
                    && c.PerkId == perkId && c.StyleDescription == style)
                ?? db.PerkSelectionCatalogs
                    .FirstOrDefault(c =>
                        c.StyleId == styleId && c.SelectionIndex == index
                        && c.PerkId == perkId && c.StyleDescription == style);
            if (catalog is null)
            {
                catalog = new PerkSelectionCatalog
                {
                    StyleId = styleId,
                    SelectionIndex = index,
                    PerkId = perkId,
                    StyleDescription = style,
                };
                db.PerkSelectionCatalogs.Add(catalog);
            }

            db.ParticipantPerkSelections.Add(new ParticipantPerkSelection
            {
                Id = Guid.NewGuid(),
                MatchId = matchId,
                ParticipantId = 1,
                Catalog = catalog,
            });
        }

        await db.SaveChangesAsync();
    }

    private static ItemEvent Purchase(int timestampMs, int itemId)
        => new() { TimestampMs = timestampMs, EventType = "ITEM_PURCHASED", ItemId = itemId };

    private static SkillEvent Skill(int timestampMs, int slot)
        => new() { TimestampMs = timestampMs, SkillSlot = slot, LevelUpType = "NORMAL" };

    /// <summary>
    /// Deterministic metadata source: serves a minimal item catalog for
    /// <see cref="KnownVersion"/> and fails for anything else, like
    /// CommunityDragon does for patches that aged out of the CDN.
    /// </summary>
    private sealed class FakeItemMetadataProvider : IItemMetadataProvider
    {
        private static readonly IReadOnlyDictionary<int, ItemMetadata> Items =
            new Dictionary<int, ItemMetadata>
            {
                // Id, PriceTotal, InStore, IsConsumable, IsBootsItem, IsBaseBoots, IsFinalItem, IsFinalBoots
                [1055] = new(1055, 450, true, false, false, false, true, false) { IsStarterClassItem = true },
                [2003] = new(2003, 50, true, true, false, false, true, false),
                [1001] = new(1001, 300, true, false, true, true, false, false),
                [3006] = new(3006, 1100, true, false, true, false, true, true),
                [3363] = new(3363, 0, true, false, false, false, false, false),
                [3031] = new(3031, 3400, true, false, false, false, true, false),
                [3153] = new(3153, 3200, true, false, false, false, true, false),
                [3072] = new(3072, 3300, true, false, false, false, true, false),
                [3026] = new(3026, 3100, true, false, false, false, true, false),
            };

        public Task<IReadOnlyDictionary<int, ItemMetadata>> GetItemsAsync(
            string gameVersion, CancellationToken ct)
            => gameVersion.StartsWith("16.4", StringComparison.Ordinal)
                ? Task.FromResult(Items)
                : Task.FromException<IReadOnlyDictionary<int, ItemMetadata>>(
                    new HttpRequestException("patch aged out"));
    }
}
