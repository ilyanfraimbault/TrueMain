using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using TrueMain.ReadModels.Champions;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Riot ID resolution across every route that takes one (#1230). Nine truemain
/// routes used to compare the name tag with <c>==</c> under Postgres' default
/// case-sensitive collation while the mains comparison lowered both halves, so
/// <c>Name#tag</c> answered in the comparison and 404'd on
/// <c>/truemains/Name-tag/profile</c>. Every route now goes through
/// <c>TruemainAccountResolver</c>, and the settled semantics are
/// case-insensitive — this fixture is what keeps a future copy of the lookup
/// from quietly reintroducing the split.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class TruemainAccountResolutionApiIntegrationTests
{
    private const int Champion = 157; // Yone

    // Stored exactly as Riot spells it. Every request below spells it otherwise.
    private const string StoredGameName = "PhantasmMain";
    private const string StoredTagLine = "EuW1";

    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-1230-4000-8000-000000000001");

    private readonly PostgresFixture _fixture;

    public TruemainAccountResolutionApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("PhantasmMain-EuW1")] // exactly as stored
    [InlineData("phantasmmain-euw1")] // what a lower-casing URL bar hands back
    [InlineData("PHANTASMMAIN-EUW1")] // what a shouted link carries
    [InlineData("PhAnTaSmMaIn-eUw1")] // anything in between
    public async Task Every_truemain_route_resolves_the_account_whatever_the_casing(string nameTag)
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAccountAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var profileResponse = await client.GetAsync($"/truemains/{nameTag}/profile");
        profileResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // The identity comes from the stored row, not from the request, so the
        // page shows the Riot ID as Riot spells it whatever the URL said.
        var profile = await profileResponse.Content.ReadFromJsonAsync<ProfileReadModel>();
        profile.Should().NotBeNull();
        profile!.Identity.GameName.Should().Be(StoredGameName);
        profile.Identity.TagLine.Should().Be(StoredTagLine);
        profile.Identity.PlatformId.Should().Be("EUW1");
        profile.Identity.SummonerLevel.Should().Be(312);

        // The sibling panels of the same page: an account with no games still
        // answers 200 with an empty payload — a 404 here would mean the route
        // failed to resolve the very account the profile just rendered.
        foreach (var route in new[] { "matches", "rank-history", "activity" })
        {
            var response = await client.GetAsync($"/truemains/{nameTag}/{route}");
            response.StatusCode.Should().Be(
                HttpStatusCode.OK,
                $"/truemains/{{nameTag}}/{route} must resolve the same account as /profile");
        }

        // And the resolved account is the seeded one, not merely *an* account:
        // the rank curve read against its id carries the snapshot we stored.
        var rankHistory = await client.GetFromJsonAsync<RankHistoryReadModel>(
            $"/truemains/{nameTag}/rank-history");
        rankHistory.Should().NotBeNull();
        rankHistory!.Entries.Should().ContainSingle("the seeded account has one rank snapshot")
            .Which.Tier.Should().Be("DIAMOND");
    }

    [Theory]
    [InlineData("PhantasmMain#EuW1")]
    [InlineData("phantasmmain#euw1")]
    [InlineData("PHANTASMMAIN#EUW1")]
    public async Task Mains_comparison_resolves_the_account_whatever_the_casing(string riotId)
    {
        await _fixture.ResetDatabaseAsync();
        await SeedAccountAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var comparison = await client.GetFromJsonAsync<ChampionMainsComparisonResponse>(
            $"/champions/{Champion}/mains-comparison?account={Uri.EscapeDataString(riotId)}");

        comparison.Should().NotBeNull();
        comparison!.Status.Should().NotBe(
            ChampionComparisonStatus.UnknownAccount,
            "the account exists — a thin sample is a different answer from an unknown Riot ID");
        comparison.Player.Should().NotBeNull();
        comparison.Player!.Identity.Should().NotBeNull();
        comparison.Player.Identity!.GameName.Should().Be(StoredGameName);
        comparison.Player.Identity.PlatformId.Should().Be("EUW1");
    }

    [Fact]
    public async Task Case_insensitive_resolution_keeps_the_most_recently_active_tiebreak()
    {
        // Two rows carrying the same Riot ID in different casings — a stale row
        // left by a rename and the live one. Matching case-insensitively widens
        // the candidate set, so the tiebreak has to hold: the most recently
        // active row wins, exactly as it did when the comparison was on `==`.
        await _fixture.ResetDatabaseAsync();
        var now = DateTime.UtcNow;

        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(new RiotAccount
            {
                Id = Guid.Parse("aaaaaaaa-1230-4000-8000-000000000002"),
                Puuid = "stale-cased-puuid",
                GameName = "Rivals",
                TagLine = "KR1",
                PlatformId = "KR",
                ProfileIconId = 1,
                SummonerLevel = 700,
                CreatedAtUtc = now.AddYears(-2),
                UpdatedAtUtc = now.AddDays(-40),
                LastMatchIngestAtUtc = now.AddDays(-40),
            });
            db.RiotAccounts.Add(new RiotAccount
            {
                Id = Guid.Parse("aaaaaaaa-1230-4000-8000-000000000003"),
                Puuid = "live-cased-puuid",
                GameName = "rivals",
                TagLine = "kr1",
                PlatformId = "EUW1",
                ProfileIconId = 2,
                SummonerLevel = 50,
                CreatedAtUtc = now.AddDays(-60),
                UpdatedAtUtc = now.AddMinutes(-10),
                LastMatchIngestAtUtc = now.AddMinutes(-10),
            });
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var profile = await client.GetFromJsonAsync<ProfileReadModel>("/truemains/RIVALS-KR1/profile");

        profile.Should().NotBeNull();
        profile!.Identity.PlatformId.Should().Be("EUW1", "the EUW1 row was ingested 10 minutes ago");
        profile.Identity.GameName.Should().Be("rivals", "the identity is the winning row's own casing");
        profile.Identity.SummonerLevel.Should().Be(50);
    }

    [Fact]
    public async Task An_unheld_riot_id_still_404s_whatever_the_casing()
    {
        // Case-insensitive matching widens what resolves; it must not turn
        // "we don't hold this account" into anything but a 404.
        await _fixture.ResetDatabaseAsync();
        await SeedAccountAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/truemains/phantasmmain-na1/profile");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task SeedAccountAsync()
    {
        var now = DateTime.UtcNow;

        await using var db = _fixture.CreateDbContext();
        db.RiotAccounts.Add(new RiotAccount
        {
            Id = AccountId,
            Puuid = "resolution-puuid",
            GameName = StoredGameName,
            TagLine = StoredTagLine,
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
            RiotAccountId = AccountId,
            CapturedAtUtc = now.AddDays(-1),
            Tier = "DIAMOND",
            Division = "II",
            LeaguePoints = 72,
            Wins = 90,
            Losses = 60,
        });

        await db.SaveChangesAsync();
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420"),
                new KeyValuePair<string, string?>("ChampionsList:MinComparisonGames", "5"),
            ]);
}
