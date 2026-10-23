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
    public async Task Batch_LinksTrackedAccounts_SeedsUntrackedOnesOnIngestedPlatforms_AndThePageListsThemAll()
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
                        // The same channel in its legacy form, listed first: the handle still leads (#2177).
                        new { kind = "youtube", url = "https://www.youtube.com/c/Irelking" },
                        new { kind = "youtube", url = "https://www.youtube.com/@Irelking" },
                        new { kind = "carrier-pigeon", url = "https://example.com" },
                    },
                    accounts = new[]
                    {
                        // Riot IDs compare case-insensitively: a source's spelling must still link.
                        new { gameName = "irelking", tagLine = "euw", platformId = "EUW1" },
                        new { gameName = "IrelSmurf", tagLine = "EUW", platformId = "EUW1" },
                        new { gameName = "IrelKr", tagLine = "KR1", platformId = "MARS1" },
                        // A platform the pipeline does not ingest: stored, never seeded (#2134).
                        new { gameName = "IrelBr", tagLine = "BR1", platformId = "BR1" },
                        // A source-side JavaScript `undefined` serialised as a name (#2134).
                        new { gameName = "undefined", tagLine = "0303", platformId = "EUW1" },
                    },
                },
            },
        });

        batch.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await batch.Content.ReadFromJsonAsync<PersonaBatchResponse>())!.Personas.Single();
        result.Created.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Accounts.Select(a => a.Status).Should().Equal("linked", "seeded", "invalid", "untracked", "invalid");

        var seeded = await _mongo.GetCollection<SeedRequestDocument>(MongoFixture.SeedRequestsCollection)
            .Find(FilterDefinition<SeedRequestDocument>.Empty)
            .ToListAsync();
        seeded.Should().ContainSingle(request => request.GameName == "IrelSmurf" && request.PlatformId == "EUW1");

        using var anonymous = CreateClient(factory, authed: false);
        var page = await anonymous.GetFromJsonAsync<PersonaReadModel>("/players/irelking");
        page!.DisplayName.Should().Be("Irelking");
        page.Links.Select(l => (l.Kind, l.Url)).Should().Equal(
            ("twitch", "https://www.twitch.tv/irelking"),
            ("youtube", "https://www.youtube.com/@Irelking"),
            ("youtube", "https://www.youtube.com/c/Irelking"));

        // The seeded IrelSmurf stays off the page until it is tracked: it has no profile to open.
        var tracked = page.Accounts.Should().ContainSingle().Subject;
        tracked.GameName.Should().Be("Irelking", "a tracked account shows its stored Riot ID, not the source's spelling");
        tracked.Region.Should().Be("europe");
        tracked.Ranked!.Tier.Should().Be("CHALLENGER");
        tracked.TopChampions.Should().ContainSingle(c => c.ChampionId == 39);
    }

    [Fact]
    public async Task Page_OrdersAccountsByTheirLastGame_FromLiveRowsOrTheMatchStamp()
    {
        var now = DateTime.UtcNow;
        await using (var db = _fixture.CreateDbContext())
        {
            // The better-ranked account is the dormant one: the page still opens on the account in use.
            var dormant = Account("puuid-dormant", "Dormant", "KR1", "KR");
            dormant.LastSeenInMatchAtUtc = now.AddDays(-60);
            var current = Account("puuid-current", "Current", "KR2", "KR");
            var never = Account("puuid-never", "Never", "KR3", "KR");
            db.RiotAccounts.AddRange(dormant, current, never);
            db.RankSnapshots.AddRange(
                Snapshot(dormant, "CHALLENGER", "I", 1500),
                Snapshot(current, "MASTER", "I", 100),
                Snapshot(never, "GRANDMASTER", "I", 600));
            db.Matches.Add(new Match { Id = "KR_1", PlatformId = "KR", QueueId = 420, GameStartTimeUtc = now.AddDays(-1) });
            db.MatchParticipants.Add(new MatchParticipant { MatchId = "KR_1", Puuid = "puuid-current", RiotAccountId = current.Id, ParticipantId = 1 });
            db.Personas.Add(new Persona
            {
                Slug = "someone",
                DisplayName = "Someone",
                Source = PersonaSource.Manual,
                Accounts =
                [
                    new PersonaAccount { GameName = "Dormant", TagLine = "KR1", PlatformId = "KR", RiotAccount = dormant, Source = PersonaSource.Manual },
                    new PersonaAccount { GameName = "Current", TagLine = "KR2", PlatformId = "KR", RiotAccount = current, Source = PersonaSource.Manual },
                    new PersonaAccount { GameName = "Never", TagLine = "KR3", PlatformId = "KR", RiotAccount = never, Source = PersonaSource.Manual },
                ],
            });
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory, authed: false);

        var page = await client.GetFromJsonAsync<PersonaReadModel>("/players/someone");
        page!.Accounts.Select(a => a.GameName).Should().Equal("Current", "Dormant", "Never");
        page.Accounts[0].LastPlayedAtUtc.Should().BeCloseTo(now.AddDays(-1), TimeSpan.FromSeconds(1));
        page.Accounts[1].LastPlayedAtUtc.Should().BeCloseTo(now.AddDays(-60), TimeSpan.FromSeconds(1));
        page.Accounts[2].LastPlayedAtUtc.Should().BeNull();
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
    public async Task Batch_MovesTheUpdateStampOnlyWhenThePageChanges()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory, authed: true);

        await Upsert(client, "steady", "Steady", ("Steady", "EUW"));
        var created = await UpdatedAt("steady");

        // The weekly scraper run resends the persona unchanged: the sitemap's lastmod must not move (#1538).
        await Upsert(client, "steady", "Steady", ("steady", "euw"));
        (await UpdatedAt("steady")).Should().Be(created);

        await Upsert(client, "steady", "Steady", ("Steady", "EUW"), ("Second", "EUW"));
        (await UpdatedAt("steady")).Should().BeAfter(created, "a new account changes the page");
    }

    private async Task<DateTime> UpdatedAt(string slug)
    {
        await using var db = _fixture.CreateDbContext();
        return await db.Personas.AsNoTracking().Where(p => p.Slug == slug).Select(p => p.UpdatedAtUtc).SingleAsync();
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

    [Fact]
    public async Task Search_FindsAKnownPlayerByName_NotByATaggedQuery_AndTheDirectoryListsOnlyTrackedOnes()
    {
        await using (var db = _fixture.CreateDbContext())
        {
            var aileri = Account("puuid-aileri", "Aileri", "KR1", "KR");
            db.RiotAccounts.Add(aileri);
            db.RankSnapshots.Add(Snapshot(aileri, "CHALLENGER", "I", 900));
            db.Personas.AddRange(
                new Persona
                {
                    Slug = "irelking",
                    DisplayName = "Irelking",
                    Source = PersonaSource.DeepLol,
                    Accounts =
                    [
                        new PersonaAccount { GameName = "Aileri", TagLine = "KR1", PlatformId = "KR", RiotAccount = aileri, Source = PersonaSource.DeepLol },
                        new PersonaAccount { GameName = "irelklng", TagLine = "kr1", PlatformId = "KR", Source = PersonaSource.DeepLol },
                    ],
                },
                new Persona
                {
                    Slug = "irelia-fan",
                    DisplayName = "Irelia Fan",
                    Source = PersonaSource.Manual,
                    Accounts = [new PersonaAccount { GameName = "Nobody", TagLine = "EUW", PlatformId = "EUW1", Source = PersonaSource.Manual }],
                });
            await db.SaveChangesAsync();
        }

        await using var factory = CreateFactory();
        using var client = CreateClient(factory, authed: false);

        // The person is found by their name although none of their Riot IDs contains it — the point.
        var search = await client.GetFromJsonAsync<SearchResponse>("/truemains/search?q=irel");
        // "Irelia Fan" matches too, but none of her accounts is tracked: her page would be empty.
        var irelking = search!.Players.Should().ContainSingle().Subject;
        irelking.Persona.Slug.Should().Be("irelking");
        irelking.AccountCount.Should().Be(1, "only the tracked account is on his page");
        irelking.ProfileIconId.Should().Be(1);
        irelking.Ranked!.Tier.Should().Be("CHALLENGER");
        (await client.GetAsync("/players/irelia-fan")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await client.GetFromJsonAsync<SearchResponse>("/truemains/search?q=irel%23KR1"))!.Players
            .Should().BeEmpty("a Name#TAG asks for one account, not a person");

        var directory = await client.GetFromJsonAsync<PersonaDirectoryResponse>("/players");
        directory!.Players.Select(p => p.Slug).Should().Equal(["irelking"], "a page of untracked Riot IDs is not advertised");
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
