using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Core.Lol.Ranking;
using Data.Entities;
using Data.Ops.Mongo;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using TrueMain.ReadModels.Ops;
using TrueMain.ReadModels.Personas;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Known players (#2016): the ops intake that links a person to the Riot accounts public sources
/// list for them, the public <c>GET /players/{slug}</c>, and the persona tag the leaderboard row
/// and the profile carry.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class PersonasApiIntegrationTests : IAsyncLifetime
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;
    private readonly MongoFixture _mongo;

    public PersonasApiIntegrationTests(PostgresFixture fixture, MongoFixture mongo)
    {
        _fixture = fixture;
        _mongo = mongo;
    }

    public async ValueTask InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Batch_LinksTrackedAccounts_SeedsUntrackedOnes_AndThePageListsThemAll()
    {
        await using (var db = _fixture.CreateDbContext())
        {
            var main = Account("puuid-main", "Irelking", "EUW", "EUW1");
            db.RiotAccounts.Add(main);
            db.RankSnapshots.Add(Snapshot(main, "CHALLENGER", "I", 1200));
            db.MainChampionStats.Add(MainStat("puuid-main", "EUW1", championId: 39));
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory, authed: true);

        var batch = await client.PostAsJsonAsync("/ops/personas/batch", new
        {
            personas = new[]
            {
                new
                {
                    slug = "irelking",
                    displayName = "Irelking",
                    source = "leaguepedia",
                    seedMissing = true,
                    links = new[]
                    {
                        new { kind = "twitch", url = "https://www.twitch.tv/irelking/" },
                        new { kind = "youtube", url = "https://www.youtube.com/@Irelking" },
                        new { kind = "carrier-pigeon", url = "https://example.com" },
                    },
                    accounts = new[]
                    {
                        // Riot IDs compare case-insensitively: a source's spelling must still link.
                        new { gameName = "irelking", tagLine = "euw", platformId = "EUW1" },
                        new { gameName = "IrelSmurf", tagLine = "EUW", platformId = "EUW1" },
                        new { gameName = "IrelKr", tagLine = "KR1", platformId = "MARS1" },
                    },
                },
            },
        });

        batch.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await batch.Content.ReadFromJsonAsync<PersonaBatchResponse>())!.Personas.Single();
        result.Created.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Accounts.Select(a => a.Status).Should().Equal("linked", "seeded", "invalid");

        var seeded = await _mongo.GetCollection<SeedRequestDocument>(MongoFixture.SeedRequestsCollection)
            .Find(FilterDefinition<SeedRequestDocument>.Empty)
            .ToListAsync();
        seeded.Should().ContainSingle(request => request.GameName == "IrelSmurf" && request.PlatformId == "EUW1");

        using var anonymous = CreateClient(factory, authed: false);
        var page = await anonymous.GetFromJsonAsync<PersonaReadModel>("/players/irelking");
        page!.DisplayName.Should().Be("Irelking");
        page.Links.Select(l => (l.Kind, l.Url)).Should().Equal(
            ("twitch", "https://www.twitch.tv/irelking"),
            ("youtube", "https://www.youtube.com/@Irelking"));

        page.Accounts.Should().HaveCount(2);
        var tracked = page.Accounts[0];
        tracked.Tracked.Should().BeTrue("tracked accounts are listed first");
        tracked.GameName.Should().Be("Irelking", "a tracked account shows its stored Riot ID, not the source's spelling");
        tracked.Region.Should().Be("europe");
        tracked.Ranked!.Tier.Should().Be("CHALLENGER");
        tracked.TopChampions.Should().ContainSingle(c => c.ChampionId == 39);

        var untracked = page.Accounts[1];
        untracked.Tracked.Should().BeFalse();
        untracked.GameName.Should().Be("IrelSmurf");
        untracked.Ranked.Should().BeNull();
    }

    [Fact]
    public async Task Batch_NeverMovesAnAccountAnotherPersonaHolds_AndLinksAPendingOneOnceTracked()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory, authed: true);

        (await Upsert(client, "first", "First", ("Shared", "EUW"), ("Later", "EUW"))).Accounts
            .Select(a => a.Status).Should().Equal("pending", "pending");

        var second = await Upsert(client, "second", "Second", ("SHARED", "euw"));
        second.Accounts.Single().Status.Should().Be("conflict", "the Riot ID already belongs to another persona");

        await using (var db = _fixture.CreateDbContext())
        {
            db.RiotAccounts.Add(Account("puuid-later", "Later", "EUW", "EUW1"));
            await db.SaveChangesAsync();
        }

        // The next run need not list the account again: every upsert retries the persona's
        // unresolved accounts, which is how a seeded account ends up linked.
        await Upsert(client, "first", "First renamed");

        await using (var db = _fixture.CreateDbContext())
        {
            var persona = await db.Personas.Include(p => p.Accounts).SingleAsync(p => p.Slug == "first");
            persona.DisplayName.Should().Be("First renamed");
            persona.Accounts.Single(a => a.GameName == "Later").RiotAccountId.Should().NotBeNull();
            persona.Accounts.Single(a => a.GameName == "Shared").RiotAccountId.Should().BeNull();
            (await db.PersonaAccounts.CountAsync()).Should().Be(2, "the conflicting account was not stored twice");
        }
    }

    [Fact]
    public async Task Batch_ReportsAMalformedPersonaWithoutFailingTheRest_AndRejectsAnOversizedBatch()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory, authed: true);

        var response = await client.PostAsJsonAsync("/ops/personas/batch", new
        {
            personas = new object[]
            {
                new { slug = "Not A Slug", displayName = "X", source = "leaguepedia" },
                new { slug = "ok", displayName = "Ok", source = "lolpros" },
                new { slug = "unknown-source", displayName = "Y", source = "opgg" },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var results = (await response.Content.ReadFromJsonAsync<PersonaBatchResponse>())!.Personas;
        results.Select(r => r.Error is null).Should().Equal(false, true, false);

        var oversized = await client.PostAsJsonAsync("/ops/personas/batch", new
        {
            personas = Enumerable.Range(0, 101)
                .Select(i => new { slug = $"p{i}", displayName = $"P{i}", source = "manual" })
                .ToArray(),
        });
        oversized.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var anonymous = CreateClient(factory, authed: false);
        (await anonymous.PostAsJsonAsync("/ops/personas/batch", new { personas = Array.Empty<object>() }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/players/nobody")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LeaderboardRowAndProfile_CarryThePersonaOfALinkedAccount()
    {
        await using (var db = _fixture.CreateDbContext())
        {
            var linked = Account("puuid-linked", "Streamer", "EUW", "EUW1");
            var plain = Account("puuid-plain", "Nobody", "EUW", "EUW1");
            db.RiotAccounts.AddRange(linked, plain);
            db.RankSnapshots.AddRange(Snapshot(linked, "MASTER", "I", 300), Snapshot(plain, "MASTER", "I", 100));
            db.MainChampionStats.AddRange(MainStat("puuid-linked", "EUW1", 39), MainStat("puuid-plain", "EUW1", 39));
            db.Personas.Add(new Persona
            {
                Slug = "streamer",
                DisplayName = "The Streamer",
                Source = PersonaSource.Manual,
                Accounts =
                [
                    new PersonaAccount
                    {
                        GameName = "Streamer", TagLine = "EUW", PlatformId = "EUW1",
                        RiotAccount = linked, Source = PersonaSource.Manual,
                    },
                ],
            });
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory, authed: false);

        var leaderboard = await client.GetFromJsonAsync<LeaderboardResponse>("/truemains");
        leaderboard!.Rows.Select(r => r.Persona?.Slug).Should().Equal("streamer", null);
        leaderboard.Rows[0].Persona!.DisplayName.Should().Be("The Streamer");

        var profile = await client.GetFromJsonAsync<ProfileReadModel>("/truemains/Streamer-EUW/profile");
        profile!.Persona!.Slug.Should().Be("streamer");
        (await client.GetFromJsonAsync<ProfileReadModel>("/truemains/Nobody-EUW/profile"))!.Persona.Should().BeNull();
    }

    private static async Task<PersonaUpsertResult> Upsert(
        HttpClient client, string slug, string displayName, params (string GameName, string TagLine)[] accounts)
    {
        var response = await client.PostAsJsonAsync("/ops/personas/batch", new
        {
            personas = new[]
            {
                new
                {
                    slug,
                    displayName,
                    source = "manual",
                    accounts = accounts.Select(a => new { gameName = a.GameName, tagLine = a.TagLine, platformId = "EUW1" }).ToArray(),
                },
            },
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PersonaBatchResponse>())!.Personas.Single();
    }

    private static RiotAccount Account(string puuid, string gameName, string tagLine, string platformId)
        => new()
        {
            Id = Guid.NewGuid(),
            Puuid = puuid,
            GameName = gameName,
            TagLine = tagLine,
            PlatformId = platformId,
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            LastMatchIngestAtUtc = DateTime.UtcNow,
        };

    private static RankSnapshot Snapshot(RiotAccount account, string tier, string division, int leaguePoints)
    {
        account.Score = RankScore.Compute(tier, division, leaguePoints);
        return new RankSnapshot
        {
            Id = Guid.NewGuid(),
            RiotAccount = account,
            CapturedAtUtc = DateTime.UtcNow,
            Tier = RankTiers.ParseTier(tier),
            Division = RankTiers.ParseDivision(division),
            LeaguePoints = leaguePoints,
            Wins = 60,
            Losses = 40,
        };
    }

    private static MainChampionStat MainStat(string puuid, string platformId, int championId)
        => new()
        {
            Id = Guid.NewGuid(),
            PlatformId = platformId,
            Puuid = puuid,
            ChampionId = championId,
            TotalMatches = 100,
            ChampionMatches = 100,
            PlayRate = 1d,
            IsMain = true,
            IsOtp = true,
            PrimaryPosition = "TOP",
            PositionBreakdown = [new PositionStat { Position = "TOP", Games = 100, Rate = 1d }],
            CalculatedAtUtc = DateTime.UtcNow,
        };

    private ApiWebApplicationFactory CreateFactory() => new(_fixture, _mongo);

    private static HttpClient CreateClient(ApiWebApplicationFactory factory, bool authed)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        if (authed)
        {
            client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        }

        return client;
    }

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture, MongoFixture mongo)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MongoLogging:ConnectionString", mongo.ConnectionString),
                new KeyValuePair<string, string?>("MongoLogging:Database", MongoFixture.DatabaseName),
                new KeyValuePair<string, string?>("MongoLogging:MinimumLevel", "None"),
                new KeyValuePair<string, string?>("MainAnalysis:QueueId", "420"),
                new KeyValuePair<string, string?>("TruemainsLeaderboard:MinRankedGames", "0"),
            ]);
}
