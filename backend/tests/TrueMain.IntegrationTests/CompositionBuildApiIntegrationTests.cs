using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.BuildFacts;
using Data.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TrueMain.ReadModels.Champions;
using TrueMain.TestKit.EntityBuilders;

namespace TrueMain.IntegrationTests;

/// <summary>
/// End-to-end coverage of <c>POST /champions/{id}/composition-build</c> (#563):
/// the full pipeline — similarity selection over seeded games, win-weighted
/// aggregation, confidence block — plus the input validation surface. Item
/// metadata is faked so the test never talks to CommunityDragon.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class CompositionBuildApiIntegrationTests
{
    private const int Champion = 157; // Yone
    private const int RoleOpponent = 238; // Zed
    private const int OtherOpponent = 91; // Talon
    private const string Position = "MIDDLE";

    private readonly PostgresFixture _fixture;

    public CompositionBuildApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PostCompositionBuild_ReturnsWinWeightedRecommendationWithConfidence()
    {
        await _fixture.ResetDatabaseAsync();

        // Two wins vs the requested role opponent on the same build, one loss
        // vs another mid on a different build. The role opponent is a hard
        // requirement: only the two matchup games enter the sample, and the
        // win build carries the recommendation.
        await SeedGameAsync("COMPE_WIN1", win: true, enemyMid: RoleOpponent, buildOrder: [3031, 3153]);
        await SeedGameAsync("COMPE_WIN2", win: true, enemyMid: RoleOpponent, buildOrder: [3031, 3153]);
        await SeedGameAsync("COMPE_LOSS", win: false, enemyMid: OtherOpponent, buildOrder: [3072, 3026]);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync(
            $"/champions/{Champion}/composition-build",
            new
            {
                position = "middle", // canonicalised to MIDDLE
                enemies = new[] { new { championId = RoleOpponent, position = "MIDDLE" } },
            });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CompositionBuildResponse>();
        result.Should().NotBeNull();
        result!.ChampionId.Should().Be(Champion);
        result.Position.Should().Be(Position);
        result.EloBracket.Should().Be("all", "the resolved token mirrors the sibling champion endpoints");

        result.MatchupRequested.Should().BeTrue();
        result.MatchupFound.Should().BeTrue();

        result.Confidence.SampleSize.Should().Be(2, "the non-matchup loss is hard-filtered out");
        result.Confidence.CandidatePoolSize.Should().Be(3);
        result.Confidence.MaxPossibleScore.Should().Be(10, "one lane-opponent slot was requested");
        result.Confidence.MeanSimilarity.Should().BeApproximately(1d, 1e-9);

        result.Build.GamesConsidered.Should().Be(2);
        result.Build.Wins.Should().Be(2);
        result.Build.CorePath.Should().NotBeNull();
        result.Build.CorePath!.ItemIds.Should().Equal(3031, 3153);
        result.Build.Boots!.ItemIds.Should().Equal(3006);
        result.Build.StarterItems!.ItemIds.Should().Contain(1055);
        result.Build.SummonerSpells!.Spell1Id.Should().Be(4);
        result.Build.SummonerSpells.Spell2Id.Should().Be(12);
        result.Build.SkillOrder!.Sequence.Should().Equal("Q", "W", "E");
        result.Build.RunePage.Should().NotBeNull();
        result.Build.RunePage!.PrimaryKeystoneId.Should().Be(8010);
    }

    [Fact]
    public async Task PostCompositionBuildGames_ListsTheSelectionWithScoresAndPilots()
    {
        await _fixture.ResetDatabaseAsync();

        await SeedGameAsync("COMPG_MAIN", win: true, enemyMid: RoleOpponent, buildOrder: [3031, 3153]);
        await SeedGameAsync("COMPG_OTHER", win: false, enemyMid: RoleOpponent, buildOrder: [3072, 3026]);
        // The non-matchup game is hard-filtered out of the selection, so the
        // listing must not show it either — the drawer's whole point is that
        // it lists the games the build was actually computed from.
        await SeedGameAsync("COMPG_OFF", win: true, enemyMid: OtherOpponent, buildOrder: [3072, 3026]);

        // One of the two pilots is an active main of the champion: mains lead
        // the selection order, and the row says so.
        await SeedPilotAsync("COMPG_MAIN", gameName: "Sheiden", tagLine: "1234", isMain: true);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync(
            $"/champions/{Champion}/composition-build/games?page=1&pageSize=10",
            new
            {
                position = Position,
                enemies = new[] { new { championId = RoleOpponent, position = "MIDDLE" } },
            });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CompositionBuildGamesResponse>();
        result.Should().NotBeNull();
        result!.Total.Should().Be(2, "the non-matchup game never entered the selection");
        result.Page.Should().Be(1);
        result.MaxPossibleScore.Should().Be(10, "one role-opponent slot was requested");
        result.Games.Should().HaveCount(2);

        var first = result.Games[0];
        first.Match.MatchId.Should().Be("COMPG_MAIN", "mains lead the selection order");
        first.IsTruemain.Should().BeTrue();
        first.Score.Should().Be(10, "the game reproduces the only requested slot");
        first.Pilot.Should().NotBeNull();
        first.Pilot!.GameName.Should().Be("Sheiden");
        first.Pilot.TagLine.Should().Be("1234");
        first.Match.Self.ChampionId.Should().Be(Champion, "the row renders the pilot's own slice");
        first.Match.Self.Win.Should().BeTrue();
        first.Match.Participants.Should().HaveCount(10);

        var second = result.Games[1];
        second.Match.MatchId.Should().Be("COMPG_OTHER");
        second.IsTruemain.Should().BeFalse();
        second.Pilot.Should().BeNull("that game's puuid has no Riot account row");
    }

    [Fact]
    public async Task PostCompositionBuildGames_InvalidDraft_Returns400()
    {
        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // Same validation as the recommendation itself — the two share it.
        var response = await client.PostAsJsonAsync(
            $"/champions/{Champion}/composition-build/games",
            new { position = "NOT_A_LANE" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostCompositionBuild_EmptyPoolAndDraft_ReturnsHonestlyEmptyRecommendation()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync(
            $"/champions/{Champion}/composition-build",
            new { position = Position });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CompositionBuildResponse>();
        result!.Confidence.SampleSize.Should().Be(0);
        result.Confidence.CandidatePoolSize.Should().Be(0);
        result.Confidence.MaxPossibleScore.Should().Be(0, "no composition slot was provided");
        result.Confidence.MeanSimilarity.Should().Be(0);
        result.Build.CorePath.Should().BeNull();
        result.Build.RunePage.Should().BeNull();
    }

    [Fact]
    public async Task PostCompositionBuild_ExplicitNullSlotLists_AreTreatedAsEmpty()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // An explicit "allies": null / "enemies": null in the body overrides
        // the DTO's empty-list default at binding time — it must read as an
        // empty draft, not crash the validation into a 500.
        var response = await client.PostAsJsonAsync(
            $"/champions/{Champion}/composition-build",
            new { position = Position, allies = (object?)null, enemies = (object?)null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<CompositionBuildResponse>();
        result!.Confidence.MaxPossibleScore.Should().Be(0);
    }

    [Fact]
    public async Task PostCompositionBuild_NullSlotEntry_Returns400()
    {
        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync(
            $"/champions/{Champion}/composition-build",
            new { position = Position, enemies = new object?[] { null } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("NOT_A_LANE")]
    public async Task PostCompositionBuild_InvalidPlayerPosition_Returns400(string? position)
    {
        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync(
            $"/champions/{Champion}/composition-build",
            new { position });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostCompositionBuild_InvalidSlots_Return400()
    {
        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        // Non-positive champion id in a slot.
        (await client.PostAsJsonAsync(
            $"/champions/{Champion}/composition-build",
            new
            {
                position = Position,
                enemies = new[] { new { championId = 0, position = "TOP" } },
            })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Duplicate position within one team.
        (await client.PostAsJsonAsync(
            $"/champions/{Champion}/composition-build",
            new
            {
                position = Position,
                enemies = new[]
                {
                    new { championId = 266, position = "TOP" },
                    new { championId = 86, position = "TOP" },
                },
            })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Ally slot at the player's own position.
        (await client.PostAsJsonAsync(
            $"/champions/{Champion}/composition-build",
            new
            {
                position = Position,
                allies = new[] { new { championId = 103, position = Position } },
            })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

    /// <summary>
    /// Seeds a full 10-participant ranked game: the searched champion mid on
    /// team 100 (participant 1) with timeline events, spells, and the
    /// Conqueror-style rune page; the requested enemy mid on team 200;
    /// distinct filler champions everywhere else.
    /// </summary>
    [Fact]
    public async Task PostCompositionBuild_JudgesTheLaneOverTheSampledGamesThemselves()
    {
        await _fixture.ResetDatabaseAsync();

        // Three games of the matchup: one lane clearly won, one clearly lost, one
        // inside the threshold band. The third is the reason `decidedGames` is its
        // own counter — it is measurable, and it decides nothing.
        await SeedGameAsync("COMPL_WON", win: true, enemyMid: RoleOpponent, buildOrder: [3031, 3153]);
        await SeedGameAsync("COMPL_LOST", win: false, enemyMid: RoleOpponent, buildOrder: [3031, 3153]);
        await SeedGameAsync("COMPL_EVEN", win: true, enemyMid: RoleOpponent, buildOrder: [3031, 3153]);
        // Ahead in gold, behind in XP on the won lane — the disagreement the two
        // figures exist for, and proof neither is derived from the other.
        await SeedLaneSnapshotsAsync("COMPL_WON", selfGold: 6_000, opponentGold: 5_000, selfXp: 7_400, opponentXp: 8_000);
        await SeedLaneSnapshotsAsync("COMPL_LOST", selfGold: 5_000, opponentGold: 6_000, selfXp: 7_000, opponentXp: 8_000);
        await SeedLaneSnapshotsAsync("COMPL_EVEN", selfGold: 5_100, opponentGold: 5_000, selfXp: 8_000, opponentXp: 8_000);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync(
            $"/champions/{Champion}/composition-build",
            new
            {
                position = Position,
                enemies = new[] { new { championId = RoleOpponent, position = Position } },
            });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<CompositionBuildResponse>();
        var lane = payload!.Lane;

        lane.MeasuredGames.Should().Be(3, "all three games have both 15-minute readings");
        lane.DecidedGames.Should().Be(2, "the +100 gold lane is inside the threshold and decides nothing");
        lane.WinRate.Should().BeApproximately(0.5d, 1e-9, "one of the two decided lanes was won");

        // Averaged over the measured games, evens included — an even lane is exactly
        // what the win rate above cannot express.
        lane.AverageGoldDiffAt15.Should().BeApproximately((1_000 - 1_000 + 100) / 3d, 1e-9);
        lane.AverageXpDiffAt15.Should().BeApproximately((-600 - 1_000 + 0) / 3d, 1e-9);
    }

    [Fact]
    public async Task PostCompositionBuild_ReportsAnUnjudgeableLaneAsUnknownRatherThanEven()
    {
        await _fixture.ResetDatabaseAsync();
        // A sampled game whose timeline was never ingested: a real game, not a lane
        // anyone can call. Zeroes here would print a dead-even lane out of nothing.
        await SeedGameAsync("COMPL_NOSNAP", win: true, enemyMid: RoleOpponent, buildOrder: [3031, 3153]);

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var payload = await (await client.PostAsJsonAsync(
            $"/champions/{Champion}/composition-build",
            new
            {
                position = Position,
                enemies = new[] { new { championId = RoleOpponent, position = Position } },
            })).Content.ReadFromJsonAsync<CompositionBuildResponse>();

        payload!.Build.GamesConsidered.Should().Be(1, "the game itself is still sampled");
        payload.Lane.MeasuredGames.Should().Be(0);
        payload.Lane.WinRate.Should().BeNull();
        payload.Lane.AverageGoldDiffAt15.Should().BeNull();
        payload.Lane.AverageXpDiffAt15.Should().BeNull();
    }

    /// <summary>
    /// The two 15-minute snapshots a lane is judged from (#1117): the searched
    /// participant (always id 1) and the enemy on the same lane (id 8 — team 200 is
    /// seeded TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY from 6). Gold and XP are set apart
    /// independently so a game can be ahead in one and behind in the other.
    /// </summary>
    private async Task SeedLaneSnapshotsAsync(
        string matchId, int selfGold, int opponentGold, int selfXp, int opponentXp)
    {
        await using var db = _fixture.CreateDbContext();

        db.MatchParticipantTimelineSnapshots.AddRange(
            LaneSnapshot(matchId, participantId: 1, selfGold, selfXp),
            LaneSnapshot(matchId, participantId: 8, opponentGold, opponentXp));

        await db.SaveChangesAsync();
    }

    private static MatchParticipantTimelineSnapshot LaneSnapshot(
        string matchId, int participantId, int totalGold, int xp)
        => new()
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            ParticipantId = participantId,
            IntervalMinute = 15,
            TimestampMs = 15 * 60 * 1000,
            TotalGold = totalGold,
            MinionsKilled = 100,
            JungleMinionsKilled = 0,
            Level = 11,
            Xp = xp,
            Kills = 2,
            DamageToChampions = 5000,
        };

    private async Task SeedGameAsync(string matchId, bool win, int enemyMid, int[] buildOrder)
    {
        await using var db = _fixture.CreateDbContext();

        db.Matches.Add(new MatchBuilder()
            .WithId(matchId)
            .WithGameStartTimeUtc(DateTime.UtcNow.AddDays(-1))
            .Build());

        var itemEvents = new List<ItemEvent>
        {
            new() { TimestampMs = 10_000, EventType = "ITEM_PURCHASED", ItemId = 1055 },
            new() { TimestampMs = 600_000, EventType = "ITEM_PURCHASED", ItemId = 3006 },
        };
        for (var i = 0; i < buildOrder.Length; i++)
        {
            itemEvents.Add(new ItemEvent
            {
                TimestampMs = 700_000 + i * 100_000,
                EventType = "ITEM_PURCHASED",
                ItemId = buildOrder[i],
            });
        }

        var skillEvents = new List<SkillEvent>
        {
            new() { TimestampMs = 60_000, SkillSlot = 1, LevelUpType = "NORMAL" },
            new() { TimestampMs = 120_000, SkillSlot = 2, LevelUpType = "NORMAL" },
            new() { TimestampMs = 180_000, SkillSlot = 1, LevelUpType = "NORMAL" },
            new() { TimestampMs = 240_000, SkillSlot = 2, LevelUpType = "NORMAL" },
            new() { TimestampMs = 300_000, SkillSlot = 3, LevelUpType = "NORMAL" },
            new() { TimestampMs = 360_000, SkillSlot = 3, LevelUpType = "NORMAL" },
        };

        string[] positions = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];
        var participantId = 1;

        db.MatchParticipants.Add(Participant(
            matchId, participantId++, Champion, 100, Position, win, itemEvents, skillEvents,
            item0: 1055, item1: 3006,
            item2: buildOrder.Length > 0 ? buildOrder[0] : 0,
            item3: buildOrder.Length > 1 ? buildOrder[1] : 0));
        foreach (var position in positions.Where(p => p != Position))
        {
            db.MatchParticipants.Add(Participant(
                matchId, participantId, 900 + participantId, 100, position, win, [], []));
            participantId++;
        }

        foreach (var position in positions)
        {
            var championId = position == Position ? enemyMid : 900 + participantId;
            db.MatchParticipants.Add(Participant(
                matchId, participantId++, championId, 200, position, !win, [], []));
        }

        await db.SaveChangesAsync();
        await SeedRunePageAsync(db, matchId);
    }

    /// <summary>
    /// Gives the searched participant of <paramref name="matchId"/> a Riot
    /// identity, optionally as an active main of the champion. The pilot is
    /// resolved by puuid, so no <c>RiotAccountId</c> back-reference is needed
    /// on the participant row — same as the harvested rows in production.
    /// </summary>
    private async Task SeedPilotAsync(string matchId, string gameName, string tagLine, bool isMain)
    {
        await using var db = _fixture.CreateDbContext();

        var puuid = $"puuid-{matchId}-1";
        db.RiotAccounts.Add(new RiotAccountBuilder()
            .WithPuuid(puuid)
            .WithGameName(gameName)
            .WithTagLine(tagLine)
            .Build());

        if (isMain)
        {
            db.MainChampionStats.Add(new MainChampionStat
            {
                Id = Guid.NewGuid(),
                PlatformId = "KR",
                Puuid = puuid,
                ChampionId = Champion,
                TotalMatches = 100,
                ChampionMatches = 60,
                PlayRate = 0.6,
                IsMain = true,
                IsActive = true,
                PrimaryPosition = Position,
                CalculatedAtUtc = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
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

    private static MatchParticipant Participant(
        string matchId,
        int participantId,
        int championId,
        int teamId,
        string teamPosition,
        bool win,
        List<ItemEvent> itemEvents,
        List<SkillEvent> skillEvents,
        int item0 = 0,
        int item1 = 0,
        int item2 = 0,
        int item3 = 0)
        => new()
        {
            MatchId = matchId,
            ParticipantId = participantId,
            Puuid = $"puuid-{matchId}-{participantId}",
            RiotAccountId = null,
            SummonerName = "seed",
            SummonerLevel = 100,
            ChampionId = championId,
            TeamId = teamId,
            TeamPosition = teamPosition,
            IndividualPosition = teamPosition,
            Lane = teamPosition,
            Role = "SOLO",
            Win = win,
            Kills = 5,
            Deaths = 4,
            Assists = 6,
            GoldEarned = 12000,
            TotalMinionsKilled = 180,
            NeutralMinionsKilled = 4,
            ChampLevel = 16,
            Item0 = item0,
            Item1 = item1,
            Item2 = item2,
            Item3 = item3,
            Item4 = 0,
            Item5 = 0,
            Item6 = 0,
            TrinketItemId = 3363,
            PerksDefense = 5001,
            PerksFlex = 5008,
            PerksOffense = 5005,
            PrimaryStyleId = 8000,
            SubStyleId = 8100,
            Summoner1Id = 12, // canonicalised to (4, 12) by the pipeline
            Summoner2Id = 4,
            EloBracket = "",
            ItemEvents = itemEvents,
            SkillEvents = skillEvents,
        };

    /// <summary>
    /// API factory with a deterministic item-metadata source so the endpoint
    /// never calls CommunityDragon from the test suite.
    /// </summary>
    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(fixture)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IItemMetadataProvider>();
                services.AddSingleton<IItemMetadataProvider>(new FakeItemMetadataProvider());
            });
        }
    }

    private sealed class FakeItemMetadataProvider : IItemMetadataProvider
    {
        private static readonly IReadOnlyDictionary<int, ItemMetadata> Items =
            new Dictionary<int, ItemMetadata>
            {
                // Id, PriceTotal, InStore, IsConsumable, IsBootsItem, IsBaseBoots, IsFinalItem, IsFinalBoots
                [1055] = new(1055, 450, true, false, false, false, true, false) { IsStarterClassItem = true },
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
            => Task.FromResult(Items);
    }
}
