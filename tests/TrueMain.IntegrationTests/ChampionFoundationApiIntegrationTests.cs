using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TrueMain.Contracts.Champions;
using Data.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TrueMain.IntegrationTests;

public sealed class ChampionFoundationApiIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public ChampionFoundationApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetFoundationAsync_ShouldReturnChampionFoundationContracts()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedChampionFoundationAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/champions/22");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(["summary", "howToPlay"]);

        var summaryProperties = root.GetProperty("summary").EnumerateObject().Select(property => property.Name);
        summaryProperties.Should().BeEquivalentTo(
            ["championId", "games", "winRate", "specialistCount", "otpCount", "primaryPosition", "latestPatchVersion", "lastUpdatedAtUtc"]);

        var howToPlayProperties = root.GetProperty("howToPlay").EnumerateObject().Select(property => property.Name);
        howToPlayProperties.Should().BeEquivalentTo(
            ["sampleSize", "coreSummonerSpells", "coreSkillOrder", "coreItemSet", "summonerSpellOptions", "skillOrderOptions", "itemSetOptions"]);

        root.GetProperty("howToPlay").GetProperty("coreSummonerSpells").EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["spell1Id", "spell2Id", "games", "playRate", "winRate"]);
        root.GetProperty("howToPlay").GetProperty("coreSkillOrder").EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["sequence", "games", "playRate", "winRate"]);
        root.GetProperty("howToPlay").GetProperty("coreItemSet").EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["itemIds", "games", "playRate", "winRate"]);
        AssertObjectArrayElementsHaveProperties(root.GetProperty("howToPlay").GetProperty("summonerSpellOptions"),
            "spell1Id", "spell2Id", "games", "playRate", "winRate");
        AssertObjectArrayElementsHaveProperties(root.GetProperty("howToPlay").GetProperty("skillOrderOptions"),
            "sequence", "games", "playRate", "winRate");
        AssertObjectArrayElementsHaveProperties(root.GetProperty("howToPlay").GetProperty("itemSetOptions"),
            "itemIds", "games", "playRate", "winRate");

        var payload = await response.Content.ReadFromJsonAsync<ChampionFoundationResponse>();
        payload.Should().NotBeNull();
        payload!.Summary.ChampionId.Should().Be(22);
        payload.Summary.Games.Should().Be(3);
        payload.Summary.SpecialistCount.Should().Be(2);
        payload.Summary.OtpCount.Should().Be(1);
        payload.Summary.PrimaryPosition.Should().Be("BOTTOM");
        payload.Summary.LatestPatchVersion.Should().Be("16.4");
        payload.HowToPlay.SampleSize.Should().Be(3);
        payload.HowToPlay.CoreSummonerSpells.Should().NotBeNull();
        payload.HowToPlay.CoreSummonerSpells!.Spell1Id.Should().Be(4);
        payload.HowToPlay.CoreSummonerSpells.Spell2Id.Should().Be(7);
        payload.HowToPlay.CoreSkillOrder.Should().NotBeNull();
        payload.HowToPlay.CoreSkillOrder!.Sequence.Should().Equal("Q", "W", "E");
        payload.HowToPlay.CoreItemSet.Should().NotBeNull();
        payload.HowToPlay.CoreItemSet!.ItemIds.Should().ContainInOrder(6672, 3006, 3094);
        payload.HowToPlay.SummonerSpellOptions.Should().OnlyContain(option => option.Spell1Id == 4 && option.Spell2Id == 7);
        payload.HowToPlay.ItemSetOptions.Should().OnlyContain(option => option.ItemIds.Contains(6672));
    }

    [Fact]
    public async Task GetFoundationAsync_ShouldUseWholePatchAndDeterministicTieBreakers()
    {
        await _fixture.ResetDatabaseAsync();
        await SeedChampionFoundationTieScenarioAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var payload = await client.GetFromJsonAsync<ChampionFoundationResponse>("/champions/55");

        payload.Should().NotBeNull();
        payload!.Summary.Games.Should().Be(4);
        payload.Summary.LatestPatchVersion.Should().Be("16.5");
        payload.HowToPlay.SampleSize.Should().Be(4);
        payload.HowToPlay.CoreSummonerSpells.Should().NotBeNull();
        payload.HowToPlay.CoreSummonerSpells!.Spell1Id.Should().Be(4);
        payload.HowToPlay.CoreSummonerSpells.Spell2Id.Should().Be(7);
        payload.HowToPlay.CoreSkillOrder.Should().NotBeNull();
        payload.HowToPlay.CoreSkillOrder!.Sequence.Should().Equal("Q", "W", "E");
        payload.HowToPlay.CoreItemSet.Should().NotBeNull();
        payload.HowToPlay.CoreItemSet!.ItemIds.Should().ContainInOrder(1001, 2003, 3006);
    }

    [Fact]
    public async Task GetFoundationAsync_ShouldReturnNotFound_WhenChampionDataDoesNotExist()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/champions/999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task SeedChampionFoundationAsync()
    {
        var now = DateTime.UtcNow;

        await using var db = _fixture.CreateDbContext();
        db.RiotAccounts.AddRange(
            new RiotAccount
            {
                PlatformId = "KR",
                Puuid = "puuid-1",
                GameName = "otp-one",
                SummonerId = "summoner-1",
                ProfileIconId = 1,
                SummonerLevel = 100,
                LastProfileSyncAtUtc = now,
                CreatedAtUtc = now.AddDays(-10),
                UpdatedAtUtc = now.AddDays(-1)
            },
            new RiotAccount
            {
                PlatformId = "KR",
                Puuid = "puuid-2",
                GameName = "main-two",
                SummonerId = "summoner-2",
                ProfileIconId = 2,
                SummonerLevel = 200,
                LastProfileSyncAtUtc = now,
                CreatedAtUtc = now.AddDays(-8),
                UpdatedAtUtc = now.AddDays(-1)
            });

        db.MainChampionStats.AddRange(
            new MainChampionStat
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
                CalculatedAtUtc = now.AddMinutes(-10)
            },
            new MainChampionStat
            {
                PlatformId = "KR",
                Puuid = "puuid-2",
                ChampionId = 22,
                TotalMatches = 10,
                ChampionMatches = 6,
                PlayRate = 0.6,
                IsMain = true,
                IsOtp = false,
                PrimaryPosition = "BOTTOM",
                CalculatedAtUtc = now.AddMinutes(-5)
            });

        db.Matches.AddRange(
            new Match
            {
                Id = "KR_1",
                PlatformId = "KR",
                QueueId = 420,
                MapId = 11,
                GameMode = "CLASSIC",
                GameType = "MATCHED_GAME",
                GameStartTimeUtc = now.AddHours(-3),
                GameDurationSeconds = 1800,
                GameVersion = "16.4.1",
                CreatedAtUtc = now.AddHours(-3),
                TimelineIngested = true
            },
            new Match
            {
                Id = "KR_2",
                PlatformId = "KR",
                QueueId = 420,
                MapId = 11,
                GameMode = "CLASSIC",
                GameType = "MATCHED_GAME",
                GameStartTimeUtc = now.AddHours(-2),
                GameDurationSeconds = 1850,
                GameVersion = "16.4.2",
                CreatedAtUtc = now.AddHours(-2),
                TimelineIngested = true
            },
            new Match
            {
                Id = "KR_3",
                PlatformId = "KR",
                QueueId = 420,
                MapId = 11,
                GameMode = "CLASSIC",
                GameType = "MATCHED_GAME",
                GameStartTimeUtc = now.AddHours(-1),
                GameDurationSeconds = 1900,
                GameVersion = "16.4.1",
                CreatedAtUtc = now.AddHours(-1),
                TimelineIngested = true
            },
            new Match
            {
                Id = "KR_4",
                PlatformId = "KR",
                QueueId = 420,
                MapId = 11,
                GameMode = "CLASSIC",
                GameType = "MATCHED_GAME",
                GameStartTimeUtc = now.AddDays(-5),
                GameDurationSeconds = 1750,
                GameVersion = "16.3.9",
                CreatedAtUtc = now.AddDays(-5),
                TimelineIngested = true
            },
            new Match
            {
                Id = "KR_5",
                PlatformId = "KR",
                QueueId = 450,
                MapId = 12,
                GameMode = "ARAM",
                GameType = "MATCHED_GAME",
                GameStartTimeUtc = now.AddMinutes(-10),
                GameDurationSeconds = 1200,
                GameVersion = "16.4.9",
                CreatedAtUtc = now.AddMinutes(-10),
                TimelineIngested = true
            });

        db.MatchParticipants.AddRange(
            BuildParticipant(Guid.Parse("11111111-1111-1111-1111-111111111111"), "KR_1", 1, "puuid-1", true),
            BuildParticipant(Guid.Parse("22222222-2222-2222-2222-222222222222"), "KR_2", 1, "puuid-1", false),
            BuildParticipant(Guid.Parse("33333333-3333-3333-3333-333333333333"), "KR_3", 1, "puuid-2", true),
            BuildParticipant(Guid.Parse("44444444-4444-4444-4444-444444444444"), "KR_4", 1, "puuid-2", false, 1, 3, [1055, 3006, 3031], [2, 1, 3]),
            BuildParticipant(Guid.Parse("55555555-5555-5555-5555-555555555555"), "KR_5", 1, "puuid-1", true, 12, 4, [6672, 3085, 3031], [3, 1, 2]));

        await db.SaveChangesAsync();
    }

    private async Task SeedChampionFoundationTieScenarioAsync()
    {
        var now = DateTime.UtcNow;

        await using var db = _fixture.CreateDbContext();
        db.RiotAccounts.AddRange(
            new RiotAccount
            {
                PlatformId = "KR",
                Puuid = "tie-puuid-1",
                GameName = "tie-one",
                SummonerId = "tie-summoner-1",
                ProfileIconId = 1,
                SummonerLevel = 100,
                LastProfileSyncAtUtc = now,
                CreatedAtUtc = now.AddDays(-10),
                UpdatedAtUtc = now.AddDays(-1)
            },
            new RiotAccount
            {
                PlatformId = "KR",
                Puuid = "tie-puuid-2",
                GameName = "tie-two",
                SummonerId = "tie-summoner-2",
                ProfileIconId = 2,
                SummonerLevel = 200,
                LastProfileSyncAtUtc = now,
                CreatedAtUtc = now.AddDays(-8),
                UpdatedAtUtc = now.AddDays(-1)
            });

        db.MainChampionStats.AddRange(
            new MainChampionStat
            {
                PlatformId = "KR",
                Puuid = "tie-puuid-1",
                ChampionId = 55,
                TotalMatches = 10,
                ChampionMatches = 8,
                PlayRate = 0.8,
                IsMain = true,
                IsOtp = true,
                PrimaryPosition = "MIDDLE",
                CalculatedAtUtc = now.AddMinutes(-10)
            },
            new MainChampionStat
            {
                PlatformId = "KR",
                Puuid = "tie-puuid-2",
                ChampionId = 55,
                TotalMatches = 10,
                ChampionMatches = 7,
                PlayRate = 0.7,
                IsMain = true,
                IsOtp = false,
                PrimaryPosition = "MIDDLE",
                CalculatedAtUtc = now.AddMinutes(-5)
            });

        db.Matches.AddRange(
            BuildMatch("KR_T1", now.AddHours(-4), "16.5.1"),
            BuildMatch("KR_T2", now.AddHours(-3), "16.5.2"),
            BuildMatch("KR_T3", now.AddHours(-2), "16.5.1"),
            BuildMatch("KR_T4", now.AddHours(-1), "16.5.2"),
            BuildMatch("KR_T5", now.AddMinutes(-30), "16.5.9", 450, 12, "ARAM"));

        db.MatchParticipants.AddRange(
            BuildParticipant(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"), "KR_T1", 1, "tie-puuid-1", true, 4, 7, [1001, 2003, 3006], [1, 2, 3], 55, "MIDDLE", "SOLO"),
            BuildParticipant(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"), "KR_T2", 1, "tie-puuid-1", false, 4, 14, [1001, 2003, 3007], [1, 3, 2], 55, "MIDDLE", "SOLO"),
            BuildParticipant(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3"), "KR_T3", 1, "tie-puuid-2", true, 7, 4, [1001, 2003, 3006], [1, 2, 3], 55, "MIDDLE", "SOLO"),
            BuildParticipant(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4"), "KR_T4", 1, "tie-puuid-2", false, 14, 4, [1001, 2003, 3007], [1, 3, 2], 55, "MIDDLE", "SOLO"),
            BuildParticipant(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa5"), "KR_T5", 1, "tie-puuid-1", true, 1, 4, [2003, 3020, 3089], [2, 2, 1], 55, "MIDDLE", "SOLO"));

        await db.SaveChangesAsync();
    }

    private static Match BuildMatch(
        string id,
        DateTime gameStartTimeUtc,
        string gameVersion,
        int queueId = 420,
        int mapId = 11,
        string gameMode = "CLASSIC")
    {
        return new Match
        {
            Id = id,
            PlatformId = "KR",
            QueueId = queueId,
            MapId = mapId,
            GameMode = gameMode,
            GameType = "MATCHED_GAME",
            GameStartTimeUtc = gameStartTimeUtc,
            GameDurationSeconds = 1800,
            GameVersion = gameVersion,
            CreatedAtUtc = gameStartTimeUtc,
            TimelineIngested = true
        };
    }

    private static void AssertObjectArrayElementsHaveProperties(JsonElement arrayElement, params string[] expectedPropertyNames)
    {
        foreach (var element in arrayElement.EnumerateArray())
        {
            element.EnumerateObject().Select(property => property.Name)
                .Should().BeEquivalentTo(expectedPropertyNames);
        }
    }

    private static MatchParticipant BuildParticipant(
        Guid id,
        string matchId,
        int participantId,
        string puuid,
        bool win,
        int summoner1Id = 4,
        int summoner2Id = 7,
        IReadOnlyList<int>? items = null,
        IReadOnlyList<int>? skillOrder = null,
        int championId = 22,
        string teamPosition = "BOTTOM",
        string role = "CARRY")
    {
        var resolvedItems = items ?? [6672, 3006, 3094, 3031, 3036, 3072];
        var resolvedSkillOrder = skillOrder ?? [1, 2, 3];

        return new MatchParticipant
        {
            Id = id,
            MatchId = matchId,
            ParticipantId = participantId,
            Puuid = puuid,
            SummonerName = puuid,
            SummonerLevel = 300,
            ChampionId = championId,
            TeamId = 100,
            TeamPosition = teamPosition,
            IndividualPosition = teamPosition,
            Lane = teamPosition,
            Role = role,
            Win = win,
            Kills = 10,
            Deaths = 2,
            Assists = 8,
            GoldEarned = 15000,
            TotalMinionsKilled = 220,
            NeutralMinionsKilled = 8,
            ChampLevel = 18,
            Item0 = resolvedItems.ElementAtOrDefault(0),
            Item1 = resolvedItems.ElementAtOrDefault(1),
            Item2 = resolvedItems.ElementAtOrDefault(2),
            Item3 = resolvedItems.ElementAtOrDefault(3),
            Item4 = resolvedItems.ElementAtOrDefault(4),
            Item5 = resolvedItems.ElementAtOrDefault(5),
            Item6 = 3363,
            TrinketItemId = 3363,
            PerksDefense = 5002,
            PerksFlex = 5008,
            PerksOffense = 5005,
            PrimaryStyleId = 8000,
            SubStyleId = 8200,
            Summoner1Id = summoner1Id,
            Summoner2Id = summoner2Id,
            ItemEvents = [],
            SkillEvents = resolvedSkillOrder
                .Select((skillSlot, index) => new SkillEvent
                {
                    TimestampMs = 75000 + (index * 45000),
                    SkillSlot = skillSlot,
                    LevelUpType = "NORMAL"
                })
                .ToList()
        };
    }

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("ConnectionStrings:TrueMain", fixture.ConnectionString),
                    new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420")
                ]);
            });
        }
    }
}
