using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class MatchDetailApiIntegrationTests
{
    private const string MatchId = "EUW1_MATCH_DETAIL_1";
    private static readonly Guid MainAccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly PostgresFixture _fixture;

    public MatchDetailApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetMatchDetail_returns_404_for_unknown_nameTag()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/truemains/Unknown-NA1/matches/{MatchId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMatchDetail_returns_404_for_unknown_matchId()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedFullMatchAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/truemains/Phantasm-EUW1/matches/EUW1_DOES_NOT_EXIST");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMatchDetail_returns_404_when_account_did_not_play_the_match()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedFullMatchAsync();

        // Add a second tracked account that is NOT a participant in the match.
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(new RiotAccount
            {
                Id = Guid.NewGuid(),
                Puuid = "outsider-puuid",
                GameName = "Outsider",
                TagLine = "NA1",
                PlatformId = "NA1",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                LastMatchIngestAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/truemains/Outsider-NA1/matches/{MatchId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMatchDetail_returns_full_participant_build_timeline_shape()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedFullMatchAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/truemains/Phantasm-EUW1/matches/{MatchId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await response.Content.ReadFromJsonAsync<MatchDetailReadModel>();
        detail.Should().NotBeNull();

        // ── Header ──────────────────────────────────────────────────────────
        detail!.MatchId.Should().Be(MatchId);
        detail.QueueId.Should().Be(420);
        detail.GameDurationSeconds.Should().Be(1800);
        detail.GameVersion.Should().Be("15.12.1");
        detail.Participants.Should().HaveCount(10);

        // Sorted blue (100) first, then red (200), by participant id.
        detail.Participants.Select(p => p.TeamId)
            .Should().BeEquivalentTo(
                new[] { 100, 100, 100, 100, 100, 200, 200, 200, 200, 200 },
                o => o.WithStrictOrdering());

        // ── The main player's row (top laner, participant 1) ────────────────
        var main = detail.Participants.Single(p => p.ParticipantId == 1);
        main.GameName.Should().Be("Phantasm");
        main.TagLine.Should().Be("EUW1");
        main.ChampionId.Should().Be(157);
        main.TeamPosition.Should().Be("TOP");
        main.Win.Should().BeTrue();
        main.Kills.Should().Be(10);
        main.Deaths.Should().Be(2);
        main.Assists.Should().Be(5);
        main.Items.Should().HaveCount(7);
        main.Items[0].Should().Be(6692);
        main.TrinketItemId.Should().Be(3340);
        main.Summoner1Id.Should().Be(4);
        main.Summoner2Id.Should().Be(12);
        main.PrimaryStyleId.Should().Be(8000);
        main.SubStyleId.Should().Be(8100);
        main.KeystoneId.Should().Be(8005, "keystone is slot 0 of the primary tree");

        // Rank (nearest snapshot to game start).
        main.Rank.Should().NotBeNull();
        main.Rank!.Tier.Should().Be("EMERALD");
        main.Rank.Division.Should().Be("III");
        main.Rank.LeaguePoints.Should().Be(42);

        // Derived per-minute (duration = 30 min).
        // Team 100 kills = 10 + 0*4 = 10; KP = (10+5)/10 = 1.5 clamped only by data.
        main.KillParticipation.Should().BeApproximately(1.5d, 1e-6);
        main.Cs.Should().Be(220); // 200 lane + 20 neutral
        main.CsPerMin.Should().BeApproximately(220d / 30d, 1e-6);
        main.GoldPerMin.Should().BeApproximately(15000d / 30d, 1e-6);
        main.DamagePerMin.Should().BeApproximately(30000d / 30d, 1e-6);
        main.VisionPerMin.Should().BeApproximately(30d / 30d, 1e-6);

        // Runes: full 6-rune page (4 primary + 2 secondary) + stat shards.
        main.Runes.Should().HaveCount(6);
        main.Runes.Select(r => r.PerkId)
            .Should().Contain(new[] { 8005, 9111, 9104, 8014, 8139, 8135 });
        main.StatPerkOffense.Should().Be(5005);
        main.StatPerkFlex.Should().Be(5008);
        main.StatPerkDefense.Should().Be(5001);

        // Build order: purchases in chronological order.
        main.ItemEvents.Should().HaveCountGreaterThanOrEqualTo(2);
        main.ItemEvents.Select(e => e.TimestampMs).Should().BeInAscendingOrder();
        main.ItemEvents[0].EventType.Should().Be("ITEM_PURCHASED");
        main.ItemEvents[0].ItemId.Should().Be(1055);

        // Skill order: Q/W/E/R sequence by level.
        main.SkillEvents.Should().HaveCountGreaterThanOrEqualTo(2);
        main.SkillEvents.Select(e => e.TimestampMs).Should().BeInAscendingOrder();
        main.SkillEvents[0].SkillSlot.Should().Be(1);

        // ── Laning @15 vs the opposing TOP (participant 6) ──────────────────
        // Main @15: cs = 130+10 = 140, gold = 6000, xp = 7000
        // Foe  @15: cs = 110+10 = 120, gold = 5500, xp = 6500
        main.Laning15.Should().NotBeNull();
        main.Laning15!.CsDiff.Should().Be(20);
        main.Laning15.GoldDiff.Should().Be(500);
        main.Laning15.XpDiff.Should().Be(500);

        // First to level 2: main's 2nd skill at 95s, foe's at 130s → main first.
        main.FirstToLevelTwo.Should().BeTrue();

        var foe = detail.Participants.Single(p => p.ParticipantId == 6);
        foe.FirstToLevelTwo.Should().BeFalse();
        foe.Laning15.Should().NotBeNull();
        foe.Laning15!.CsDiff.Should().Be(-20);
    }

    private async Task SeedFullMatchAsync()
    {
        var gameStart = new DateTime(2025, 6, 1, 18, 0, 0, DateTimeKind.Utc);

        await using var db = _fixture.CreateDbContext();

        db.Matches.Add(new Match
        {
            Id = MatchId,
            PlatformId = "EUW1",
            QueueId = 420,
            MapId = 11,
            GameMode = "CLASSIC",
            GameType = "MATCHED_GAME",
            GameStartTimeUtc = gameStart,
            GameDurationSeconds = 1800,
            GameVersion = "15.12.1",
            CreatedAtUtc = gameStart,
            TimelineIngested = true,
        });

        var positions = new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };

        for (var participantId = 1; participantId <= 10; participantId++)
        {
            var teamId = participantId <= 5 ? 100 : 200;
            var position = positions[(participantId - 1) % 5];
            var isMain = participantId == 1;

            Guid? accountId = null;
            string puuid;
            string summonerName;
            if (isMain)
            {
                accountId = MainAccountId;
                puuid = "phantasm-puuid";
                summonerName = "Phantasm";

                db.RiotAccounts.Add(new RiotAccount
                {
                    Id = MainAccountId,
                    Puuid = puuid,
                    GameName = "Phantasm",
                    TagLine = "EUW1",
                    PlatformId = "EUW1",
                    ProfileIconId = 1,
                    SummonerLevel = 300,
                    CreatedAtUtc = gameStart.AddDays(-100),
                    UpdatedAtUtc = gameStart,
                    LastMatchIngestAtUtc = gameStart,
                });

                // Two snapshots: an older one and one captured close to game
                // start. The nearest-by-time join must pick the close one.
                db.RankSnapshots.Add(new RankSnapshot
                {
                    Id = Guid.NewGuid(),
                    RiotAccountId = MainAccountId,
                    CapturedAtUtc = gameStart.AddDays(-20),
                    Tier = "PLATINUM",
                    Division = "I",
                    LeaguePoints = 10,
                });
                db.RankSnapshots.Add(new RankSnapshot
                {
                    Id = Guid.NewGuid(),
                    RiotAccountId = MainAccountId,
                    CapturedAtUtc = gameStart.AddHours(2),
                    Tier = "EMERALD",
                    Division = "III",
                    LeaguePoints = 42,
                });
            }
            else
            {
                puuid = $"puuid-{participantId}";
                summonerName = $"Player{participantId}";
            }

            var participant = new MatchParticipant
            {
                Id = Guid.NewGuid(),
                MatchId = MatchId,
                ParticipantId = participantId,
                Puuid = puuid,
                RiotAccountId = accountId,
                SummonerName = summonerName,
                SummonerLevel = 100,
                ChampionId = 156 + participantId,
                TeamId = teamId,
                TeamPosition = position,
                IndividualPosition = position,
                Lane = position,
                Role = "SOLO",
                Win = teamId == 100,
                Kills = isMain ? 10 : 0,
                Deaths = isMain ? 2 : 4,
                Assists = isMain ? 5 : 1,
                TotalDamageDealtToChampions = isMain ? 30000 : 12000,
                VisionScore = isMain ? 30 : 18,
                GoldEarned = isMain ? 15000 : 11000,
                TotalMinionsKilled = isMain ? 200 : 150,
                NeutralMinionsKilled = isMain ? 20 : 10,
                ChampLevel = 16,
                Item0 = isMain ? 6692 : 3006,
                Item1 = 3047,
                Item2 = 0,
                Item3 = 0,
                Item4 = 0,
                Item5 = 0,
                Item6 = 3340,
                TrinketItemId = 3340,
                PerksOffense = 5005,
                PerksFlex = 5008,
                PerksDefense = 5001,
                PrimaryStyleId = 8000,
                SubStyleId = 8100,
                Summoner1Id = 4,
                Summoner2Id = 12,
                ItemEvents =
                [
                    new ItemEvent { TimestampMs = 10_000, EventType = "ITEM_PURCHASED", ItemId = 1055 },
                    new ItemEvent { TimestampMs = 600_000, EventType = "ITEM_PURCHASED", ItemId = isMain ? 6692 : 3006 },
                ],
                // Two skill events: the 2nd one marks reaching level 2. The main
                // player gets there earlier than their lane opponent (id 6).
                SkillEvents =
                [
                    new SkillEvent { TimestampMs = 60_000, SkillSlot = 1, LevelUpType = "NORMAL" },
                    new SkillEvent { TimestampMs = isMain ? 95_000 : 130_000, SkillSlot = 2, LevelUpType = "NORMAL" },
                ],
            };
            db.MatchParticipants.Add(participant);

            // @15 timeline snapshot — main leads their opposing TOP by 20 cs /
            // 500 gold / 500 xp.
            db.MatchParticipantTimelineSnapshots.Add(new MatchParticipantTimelineSnapshot
            {
                Id = Guid.NewGuid(),
                MatchId = MatchId,
                ParticipantId = participantId,
                IntervalMinute = 15,
                TimestampMs = 900_000,
                TotalGold = isMain ? 6000 : 5500,
                MinionsKilled = isMain ? 130 : 110,
                JungleMinionsKilled = 10,
                Level = 9,
                Xp = isMain ? 7000 : 6500,
                Kills = 1,
                DamageToChampions = 8000,
                WardsPlaced = 3,
                WardsKilled = 1,
            });

            // Full 6-rune page: 4 primary (style 8000) + 2 secondary (style 8100).
            var primaryPerks = new[] { 8005, 9111, 9104, 8014 };
            for (var idx = 0; idx < primaryPerks.Length; idx++)
            {
                AddPerk(db, participantId, 8000, idx, primaryPerks[idx], "primaryStyle");
            }

            var secondaryPerks = new[] { 8139, 8135 };
            for (var idx = 0; idx < secondaryPerks.Length; idx++)
            {
                AddPerk(db, participantId, 8100, idx, secondaryPerks[idx], "subStyle");
            }
        }

        await db.SaveChangesAsync();
    }

    // PerkSelectionCatalog rows are deduped by (StyleId, SelectionIndex, PerkId,
    // StyleDescription); reuse an in-memory cache so the 10 participants share
    // catalog ids rather than tripping the unique index.
    private readonly Dictionary<(int Style, int Index, int Perk, string Desc), PerkSelectionCatalog> _catalog = new();

    private void AddPerk(
        Data.TrueMainDbContext db,
        int participantId,
        int styleId,
        int selectionIndex,
        int perkId,
        string styleDescription)
    {
        var key = (styleId, selectionIndex, perkId, styleDescription);
        if (!_catalog.TryGetValue(key, out var catalog))
        {
            catalog = new PerkSelectionCatalog
            {
                StyleId = styleId,
                SelectionIndex = selectionIndex,
                PerkId = perkId,
                StyleDescription = styleDescription,
            };
            db.PerkSelectionCatalogs.Add(catalog);
            _catalog[key] = catalog;
        }

        db.ParticipantPerkSelections.Add(new ParticipantPerkSelection
        {
            Id = Guid.NewGuid(),
            MatchId = MatchId,
            ParticipantId = participantId,
            Catalog = catalog,
        });
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

    private ApiWebApplicationFactory CreateFactory() => new(_fixture);

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture, [new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420")]);
}
