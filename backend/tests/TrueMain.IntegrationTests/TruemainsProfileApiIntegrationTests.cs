using System.Net;
using System.Net.Http.Json;
using Data.Entities;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.IntegrationTests;

public sealed class TruemainsProfileApiIntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public TruemainsProfileApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetProfile_returns_404_for_unknown_nameTag()
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.GetAsync("/truemains/Unknown-NA1/profile");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("NoHyphen")]
    [InlineData("-LeadingHyphen")]
    [InlineData("TrailingHyphen-")]
    public async Task GetProfile_returns_404_for_malformed_nameTag(string nameTag)
    {
        await _fixture.ResetDatabaseAsync();

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.GetAsync($"/truemains/{nameTag}/profile");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetProfile_returns_identity_ranked_mains_and_aggregated_positions()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(new RiotAccount
            {
                Id = accountId,
                Puuid = "phantasm-puuid",
                GameName = "Phantasm",
                TagLine = "EUW1",
                PlatformId = "EUW1",
                ProfileIconId = 4567,
                SummonerLevel = 312,
                CreatedAtUtc = now.AddDays(-30),
                UpdatedAtUtc = now,
                LastMatchIngestAtUtc = now,
            });

            db.RankSnapshots.Add(new RankSnapshot
            {
                Id = Guid.NewGuid(),
                RiotAccountId = accountId,
                CapturedAtUtc = now,
                Tier = "DIAMOND",
                Division = "II",
                LeaguePoints = 72,
                Wins = 90,
                Losses = 60,
            });

            // Two main champions: Yasuo (MIDDLE-leaning) and Ahri (MIDDLE-heavy).
            // After aggregation we should see MIDDLE dominating and a sliver
            // of BOTTOM from Yasuo's off-role games.
            db.MainChampionStats.Add(new MainChampionStat
            {
                Id = Guid.NewGuid(),
                PlatformId = "EUW1",
                Puuid = "phantasm-puuid",
                ChampionId = 157, // Yasuo
                TotalMatches = 200,
                ChampionMatches = 80,
                PlayRate = 0.4d,
                IsMain = true,
                IsOtp = false,
                PrimaryPosition = "MIDDLE",
                PositionBreakdown =
                [
                    new PositionStat { Position = "MIDDLE", Games = 60, Rate = 0.75d },
                    new PositionStat { Position = "BOTTOM", Games = 20, Rate = 0.25d },
                ],
                CalculatedAtUtc = now,
            });
            db.MainChampionStats.Add(new MainChampionStat
            {
                Id = Guid.NewGuid(),
                PlatformId = "EUW1",
                Puuid = "phantasm-puuid",
                ChampionId = 103, // Ahri
                TotalMatches = 200,
                ChampionMatches = 60,
                PlayRate = 0.3d,
                IsMain = true,
                IsOtp = false,
                PrimaryPosition = "MIDDLE",
                PositionBreakdown =
                [
                    new PositionStat { Position = "MIDDLE", Games = 60, Rate = 1.0d },
                ],
                CalculatedAtUtc = now,
            });
            // Non-main row that should NOT appear in the response.
            db.MainChampionStats.Add(new MainChampionStat
            {
                Id = Guid.NewGuid(),
                PlatformId = "EUW1",
                Puuid = "phantasm-puuid",
                ChampionId = 222, // Jinx
                TotalMatches = 200,
                ChampionMatches = 10,
                PlayRate = 0.05d,
                IsMain = false,
                IsOtp = false,
                PrimaryPosition = "BOTTOM",
                PositionBreakdown =
                [
                    new PositionStat { Position = "BOTTOM", Games = 10, Rate = 1.0d },
                ],
                CalculatedAtUtc = now,
            });

            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var response = await client.GetAsync("/truemains/Phantasm-EUW1/profile");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var profile = await response.Content.ReadFromJsonAsync<ProfileReadModel>();
        profile.Should().NotBeNull();

        profile!.Identity.GameName.Should().Be("Phantasm");
        profile.Identity.TagLine.Should().Be("EUW1");
        profile.Identity.PlatformId.Should().Be("EUW1");
        profile.Identity.ProfileIconId.Should().Be(4567);
        profile.Identity.SummonerLevel.Should().Be(312);

        profile.Ranked.Should().NotBeNull();
        profile.Ranked!.Tier.Should().Be("DIAMOND");
        profile.Ranked.Division.Should().Be("II");
        profile.Ranked.LeaguePoints.Should().Be(72);
        profile.Ranked.Wins.Should().Be(90);
        profile.Ranked.Losses.Should().Be(60);
        profile.Ranked.WinRate.Should().BeApproximately(0.6d, 0.001d);

        profile.Mains.Should().HaveCount(2, "Jinx is non-main and should be filtered out");
        profile.Mains[0].ChampionId.Should().Be(157, "Yasuo has higher PlayRate (0.4 vs 0.3)");
        profile.Mains[1].ChampionId.Should().Be(103);
        profile.Mains[0].IsOtp.Should().BeFalse();
        profile.Mains[0].PrimaryPosition.Should().Be("MIDDLE");

        // Aggregation: MIDDLE = 60 (Yasuo) + 60 (Ahri) = 120, BOTTOM = 20 (Yasuo).
        // Total = 140, MIDDLE rate = 120/140 ≈ 0.857, BOTTOM rate = 20/140 ≈ 0.143.
        profile.Positions.Should().HaveCount(2);
        var middle = profile.Positions.Single(p => p.Position == "MIDDLE");
        var bottom = profile.Positions.Single(p => p.Position == "BOTTOM");
        middle.Games.Should().Be(120);
        bottom.Games.Should().Be(20);
        middle.Rate.Should().BeApproximately(120d / 140d, 0.001d);
        bottom.Rate.Should().BeApproximately(20d / 140d, 0.001d);
    }

    [Fact]
    public async Task GetProfile_picks_most_recently_active_platform_on_collision()
    {
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        // Same (gameName, tagLine), two platforms. The EUW1 row was last
        // ingested 30 minutes ago; the KR row was last ingested 30 days ago.
        // The resolver should prefer EUW1 — the more recently active record.
        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(new RiotAccount
            {
                Id = Guid.NewGuid(),
                Puuid = "collision-kr-puuid",
                GameName = "Faker",
                TagLine = "KR1",
                PlatformId = "KR",
                ProfileIconId = 1,
                SummonerLevel = 700,
                CreatedAtUtc = now.AddYears(-3),
                UpdatedAtUtc = now.AddDays(-30),
                LastMatchIngestAtUtc = now.AddDays(-30),
            });
            db.RiotAccounts.Add(new RiotAccount
            {
                Id = Guid.NewGuid(),
                Puuid = "collision-euw-puuid",
                GameName = "Faker",
                TagLine = "KR1",
                PlatformId = "EUW1",
                ProfileIconId = 2,
                SummonerLevel = 50,
                CreatedAtUtc = now.AddDays(-60),
                UpdatedAtUtc = now.AddMinutes(-30),
                LastMatchIngestAtUtc = now.AddMinutes(-30),
            });
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

        var profileResponse = await client.GetAsync("/truemains/Faker-KR1/profile");
        profileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await profileResponse.Content.ReadFromJsonAsync<ProfileReadModel>();
        profile!.Identity.PlatformId.Should().Be("EUW1", "EUW1 was more recently ingested");
        profile.Identity.ProfileIconId.Should().Be(2);
        profile.Identity.SummonerLevel.Should().Be(50);

        // Same resolver must drive /matches so the two endpoints can never
        // disagree about which account a name tag means.
        var matchesResponse = await client.GetAsync("/truemains/Faker-KR1/matches");
        matchesResponse.StatusCode.Should().Be(HttpStatusCode.OK, "the account exists; an empty match list is still a 200");
    }

    private ApiWebApplicationFactory CreateFactory() => new(_fixture);

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
                    new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420"),
                    new KeyValuePair<string, string?>("Ops:ApiKey", "integration-tests-ops-key-0123456789-padding"),
                ]);
            });
        }
    }
}
