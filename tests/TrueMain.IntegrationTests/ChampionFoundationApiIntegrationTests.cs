using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TrueMain.Contracts.Champions;
using Data;
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
            ["championId", "games", "winRate", "specialistCount", "otpCount", "primaryPosition", "latestGameVersion", "lastUpdatedAtUtc"]);

        var howToPlayProperties = root.GetProperty("howToPlay").EnumerateObject().Select(property => property.Name);
        howToPlayProperties.Should().BeEquivalentTo(
            ["sampleSize", "coreSummonerSpells", "coreSkillOrder", "coreItemSet", "summonerSpellOptions", "skillOrderOptions", "itemSetOptions"]);

        var payload = await response.Content.ReadFromJsonAsync<ChampionFoundationResponse>();
        payload.Should().NotBeNull();
        payload!.Summary.ChampionId.Should().Be(22);
        payload.Summary.Games.Should().Be(3);
        payload.Summary.SpecialistCount.Should().Be(2);
        payload.Summary.OtpCount.Should().Be(1);
        payload.Summary.PrimaryPosition.Should().Be("BOTTOM");
        payload.Summary.LatestGameVersion.Should().Be("16.4.2");
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
                GameVersion = "16.4.2",
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
                GameVersion = "16.4.2",
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
            });

        db.MatchParticipants.AddRange(
            BuildParticipant(Guid.Parse("11111111-1111-1111-1111-111111111111"), "KR_1", 1, "puuid-1", true),
            BuildParticipant(Guid.Parse("22222222-2222-2222-2222-222222222222"), "KR_2", 1, "puuid-1", false),
            BuildParticipant(Guid.Parse("33333333-3333-3333-3333-333333333333"), "KR_3", 1, "puuid-2", true),
            BuildParticipant(Guid.Parse("44444444-4444-4444-4444-444444444444"), "KR_4", 1, "puuid-2", false, 1, 3, [1055, 3006, 3031], [2, 1, 3]));

        await db.SaveChangesAsync();
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
        IReadOnlyList<int>? skillOrder = null)
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
            ChampionId = 22,
            TeamId = 100,
            TeamPosition = "BOTTOM",
            IndividualPosition = "BOTTOM",
            Lane = "BOTTOM",
            Role = "CARRY",
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

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture) : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(
                [
                    new KeyValuePair<string, string?>("ConnectionStrings:TrueMain", fixture.ConnectionString)
                ]);
            });
        }
    }
}
