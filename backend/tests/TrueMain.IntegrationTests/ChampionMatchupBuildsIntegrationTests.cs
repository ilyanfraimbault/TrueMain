using AwesomeAssertions;
using Core.Lol.Map;
using Core.Options;
using Data.BuildFacts;
using Data.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using TrueMain.Services.Champions;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The matchup-scoped champion page (#923). The unit tests pin the fold; these pin the
/// selection, which is the half that cannot be checked without Postgres: a self-join
/// deciding which games count as "faced this opponent".
///
/// <para>
/// Getting that wrong is not a visible crash — it is a page that quietly answers with
/// the wrong games. So the seeds deliberately include the three ways it can go wrong:
/// the same champion on the <em>same</em> team, the same champion in another
/// <em>position</em>, and a different opponent entirely.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ChampionMatchupBuildsIntegrationTests(PostgresFixture fixture)
{
    private const int Champion = 157;
    private const int Opponent = 122;
    private const int OtherOpponent = 86;
    private const string Position = "MIDDLE";
    private const string KnownVersion = "16.4.521.123";

    private readonly PostgresFixture _fixture = fixture;

    [Fact]
    public async Task GetAsync_FoldsOnlyTheGamesAgainstThatOpponent()
    {
        await _fixture.ResetDatabaseAsync();

        // Two real games of the matchup, both on the same build.
        await SeedGameAsync("MU_1", win: true, opponentChampionId: Opponent, buildOrder: [3031, 3153]);
        await SeedGameAsync("MU_2", win: false, opponentChampionId: Opponent, buildOrder: [3031, 3153]);
        // Same champion, another opponent: must not be folded in.
        await SeedGameAsync("MU_3", win: true, opponentChampionId: OtherOpponent, buildOrder: [3072, 3026]);

        var result = await CreateService().GetAsync(
            Champion, Opponent, patch: null, Position, eloBracket: null, CancellationToken.None);

        result.Should().NotBeNull();
        result!.TotalGames.Should().Be(2);
        result.TotalWins.Should().Be(1);
        result.Builds.Should().NotBeEmpty();
        // The build from the game against the *other* opponent must be absent.
        result.Builds.Should().NotContain(build => build.FirstItemId == 3072);
    }

    [Fact]
    public async Task GetAsync_IgnoresTheOpponentChampionOnTheSameTeam()
    {
        await _fixture.ResetDatabaseAsync();

        // The champion is on this player's own team: an ally, not a matchup. Without the
        // TeamId check the self-join would count it and the page would report a lane
        // matchup that never happened.
        await SeedGameAsync("MU_ALLY", win: true, opponentChampionId: Opponent, buildOrder: [3031], opponentTeamId: 100);

        var result = await CreateService().GetAsync(
            Champion, Opponent, patch: null, Position, eloBracket: null, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_IgnoresTheOpponentChampionInAnotherPosition()
    {
        await _fixture.ResetDatabaseAsync();

        // Enemy team, but not the lane opponent — same champion bottom while ours is mid.
        await SeedGameAsync("MU_OTHERLANE", win: true, opponentChampionId: Opponent, buildOrder: [3031], opponentPosition: "BOTTOM");

        var result = await CreateService().GetAsync(
            Champion, Opponent, patch: null, Position, eloBracket: null, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ScopesToTheRequestedPatch()
    {
        await _fixture.ResetDatabaseAsync();

        await SeedGameAsync("MU_OLD", win: true, opponentChampionId: Opponent, buildOrder: [3031], gameVersion: "16.3.500.1");
        await SeedGameAsync("MU_NEW", win: true, opponentChampionId: Opponent, buildOrder: [3031], gameVersion: KnownVersion);

        var scoped = await CreateService().GetAsync(
            Champion, Opponent, patch: "16.4", Position, eloBracket: null, CancellationToken.None);
        var unscoped = await CreateService().GetAsync(
            Champion, Opponent, patch: null, Position, eloBracket: null, CancellationToken.None);

        scoped!.TotalGames.Should().Be(1);
        unscoped!.TotalGames.Should().Be(2, "no patch means every patch still retained");
    }

    [Fact]
    public async Task GetAsync_ScopesToTheRequestedElo()
    {
        await _fixture.ResetDatabaseAsync();

        await SeedGameAsync("MU_GOLD", win: true, opponentChampionId: Opponent, buildOrder: [3031], eloBracket: "GOLD");
        await SeedGameAsync("MU_IRON", win: true, opponentChampionId: Opponent, buildOrder: [3031], eloBracket: "IRON");

        var result = await CreateService().GetAsync(
            Champion, Opponent, patch: null, Position, "GOLD", CancellationToken.None);

        result!.TotalGames.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenTheMatchupHasNoGame()
    {
        await _fixture.ResetDatabaseAsync();

        var result = await CreateService().GetAsync(
            Champion, Opponent, patch: null, Position, eloBracket: null, CancellationToken.None);

        // Null, not an empty response: the page says "no data for this matchup" rather
        // than rendering an aggregated-looking page that happens to be blank.
        result.Should().BeNull();
    }

    private ChampionMatchupBuildsQueryService CreateService()
    {
        var db = _fixture.CreateDbContext();
        return new ChampionMatchupBuildsQueryService(
            db,
            new ParticipantBuildFactsLoader(
                db,
                new FakeItemMetadataProvider(),
                NullLogger<ParticipantBuildFactsLoader>.Instance),
            Microsoft.Extensions.Options.Options.Create(new MainAnalysisOptions()),
            NullLogger<ChampionMatchupBuildsQueryService>.Instance);
    }

    private async Task SeedGameAsync(
        string matchId,
        bool win,
        int opponentChampionId,
        int[] buildOrder,
        int opponentTeamId = 200,
        string opponentPosition = Position,
        string gameVersion = KnownVersion,
        string eloBracket = "")
    {
        await using var db = _fixture.CreateDbContext();

        db.Matches.Add(new Match
        {
            Id = matchId,
            PlatformId = "EUW1",
            QueueId = (int)LolQueueId.RankedSoloDuo,
            MapId = (int)LolMapId.SummonersRift,
            GameMode = "CLASSIC",
            GameType = "MATCHED_GAME",
            GameStartTimeUtc = DateTime.UtcNow.AddDays(-1),
            GameDurationSeconds = 1800,
            GameVersion = gameVersion,
            CreatedAtUtc = DateTime.UtcNow,
            TimelineIngested = true,
        });

        var itemEvents = new List<ItemEvent> { Purchase(10_000, 1055), Purchase(600_000, 3006) };
        for (var index = 0; index < buildOrder.Length; index++)
        {
            itemEvents.Add(Purchase(700_000 + index * 100_000, buildOrder[index]));
        }

        var finalItems = new int[7];
        finalItems[0] = 1055;
        finalItems[1] = 3006;
        for (var index = 0; index < buildOrder.Length && index < 5; index++)
        {
            finalItems[2 + index] = buildOrder[index];
        }

        db.MatchParticipants.Add(Participant(
            matchId, 1, Champion, teamId: 100, Position, win, eloBracket, finalItems, itemEvents));
        db.MatchParticipants.Add(Participant(
            matchId, 2, opponentChampionId, opponentTeamId, opponentPosition, !win, eloBracket, new int[7], []));

        await db.SaveChangesAsync();
        await SeedRunePageAsync(db, matchId);
    }

    private static MatchParticipant Participant(
        string matchId,
        int participantId,
        int championId,
        int teamId,
        string position,
        bool win,
        string eloBracket,
        int[] finalItems,
        List<ItemEvent> itemEvents) => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            Puuid = $"puuid-{matchId}-{participantId}",
            SummonerName = $"seed-{participantId}",
            SummonerLevel = 100,
            ChampionId = championId,
            TeamId = teamId,
            TeamPosition = position,
            IndividualPosition = position,
            Lane = position,
            Role = "SOLO",
            Win = win,
            Kills = 5,
            Deaths = 4,
            Assists = 6,
            GoldEarned = 12_000,
            TotalMinionsKilled = 180,
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
            Summoner1Id = 12,
            Summoner2Id = 4,
            EloBracket = eloBracket,
            ItemEvents = itemEvents,
            SkillEvents =
            [
                Skill(60_000, 1), Skill(120_000, 2), Skill(180_000, 1),
                Skill(240_000, 2), Skill(300_000, 3), Skill(360_000, 3),
            ],
        };

    private static async Task SeedRunePageAsync(Data.TrueMainDbContext db, string matchId)
    {
        (string Style, int Index, int PerkId)[] selections =
        [
            ("primaryStyle", 0, 8010), ("primaryStyle", 1, 8009),
            ("primaryStyle", 2, 9111), ("primaryStyle", 3, 9104),
            ("subStyle", 0, 8139), ("subStyle", 1, 8135),
        ];

        foreach (var (style, index, perkId) in selections)
        {
            var catalog = db.PerkSelectionCatalogs.Local
                    .FirstOrDefault(c => c.StyleDescription == style && c.SelectionIndex == index && c.PerkId == perkId)
                ?? db.PerkSelectionCatalogs
                    .FirstOrDefault(c => c.StyleDescription == style && c.SelectionIndex == index && c.PerkId == perkId);

            if (catalog is null)
            {
                catalog = new PerkSelectionCatalog
                {
                    StyleDescription = style,
                    SelectionIndex = index,
                    PerkId = perkId,
                };
                db.PerkSelectionCatalogs.Add(catalog);
                // Saved before the selection references it: the catalog id is an
                // identity column, so it is still 0 until this round-trip.
                await db.SaveChangesAsync();
            }

            db.ParticipantPerkSelections.Add(new ParticipantPerkSelection
            {
                MatchId = matchId,
                ParticipantId = 1,
                PerkSelectionCatalogId = catalog.Id,
            });
        }

        await db.SaveChangesAsync();
    }

    private static ItemEvent Purchase(int timestampMs, int itemId)
        => new() { TimestampMs = timestampMs, EventType = "ITEM_PURCHASED", ItemId = itemId };

    private static SkillEvent Skill(int timestampMs, int slot)
        => new() { TimestampMs = timestampMs, SkillSlot = slot };

    /// <summary>
    /// Item metadata for <see cref="KnownVersion"/> only, so a game on another patch
    /// exercises the loader's degrade-to-item-less path rather than reaching the network.
    /// </summary>
    private sealed class FakeItemMetadataProvider : IItemMetadataProvider
    {
        private static readonly IReadOnlyDictionary<int, ItemMetadata> Items =
            new Dictionary<int, ItemMetadata>
            {
                [1055] = new(1055, 450, true, false, false, false, true, false) { IsStarterClassItem = true },
                [2003] = new(2003, 50, true, true, false, false, true, false),
                [3006] = new(3006, 1100, true, false, true, false, true, true),
                [3363] = new(3363, 0, true, false, false, false, false, false),
                [3031] = new(3031, 3400, true, false, false, false, true, false),
                [3153] = new(3153, 3200, true, false, false, false, true, false),
                [3072] = new(3072, 3300, true, false, false, false, true, false),
                [3026] = new(3026, 3100, true, false, false, false, true, false),
            };

        public Task<IReadOnlyDictionary<int, ItemMetadata>> GetItemsAsync(string gameVersion, CancellationToken ct)
            => Task.FromResult(Items);
    }
}
