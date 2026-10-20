using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Core.Lol.Ranking;
using Data.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Truemains.Live;

namespace TrueMain.IntegrationTests;

/// <summary>
/// End-to-end cover for the profile's Live tab (#2103), with Riot replaced by a stub
/// handler that counts every call: the quota the endpoint spends is part of its contract
/// — one spectator call per player per 30 s, and league-v4 only for players the database
/// cannot rank.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class TruemainLiveApiIntegrationTests : IAsyncLifetime
{
    private const string SubjectPuuid = "subject-puuid";
    private const string TrackedPuuid = "tracked-puuid";
    private const int Smite = 11;
    private const int Flash = 4;
    private const int Ignite = 14;

    private readonly PostgresFixture _fixture;

    public TruemainLiveApiIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Returns404ForAnUnknownPlayerWithoutAskingRiot()
    {
        using var riot = new StubRiot(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await using var factory = CreateFactory(riot);
        using var client = CreateClient(factory);

        (await client.GetAsync("/truemains/Nobody-EUW1/live")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        riot.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadsASpectator404AsNotInGameAndServesRepeatOpensFromTheCache()
    {
        await SeedAsync();
        using var riot = new StubRiot(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var clock = new SteppingClock(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        await using var factory = CreateFactory(riot, clock);
        using var client = CreateClient(factory);

        var first = await client.GetFromJsonAsync<LiveGameReadModel>("/truemains/Subject-EUW1/live");
        var second = await client.GetFromJsonAsync<LiveGameReadModel>("/truemains/Subject-EUW1/live");

        first!.Status.Should().Be(LiveGameStatuses.NotInGame);
        first.Game.Should().BeNull();
        first.CheckedAtUtc.Should().Be(clock.GetUtcNow().UtcDateTime);
        second.Should().BeEquivalentTo(first);
        riot.Calls.Should().ContainSingle()
            .Which.Should().Be("https://euw1.api.riotgames.com/lol/spectator/v5/active-games/by-summoner/subject-puuid");

        // "Check now" inside the refresh floor is still served from the cache...
        clock.Advance(TimeSpan.FromSeconds(5));
        await client.GetFromJsonAsync<LiveGameReadModel>("/truemains/Subject-EUW1/live?refresh=true");
        riot.Calls.Should().ContainSingle();

        // ...and past it, asks Riot again.
        clock.Advance(TimeSpan.FromSeconds(10));
        var refreshed = await client.GetFromJsonAsync<LiveGameReadModel>("/truemains/Subject-EUW1/live?refresh=true");
        riot.Calls.Should().HaveCount(2);
        refreshed!.CheckedAtUtc.Should().Be(clock.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task DrawsBothTeamsWithRanksFromTheDatabaseFirstAndLaneOrder()
    {
        await SeedAsync();
        using var riot = new StubRiot(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/lol/spectator/", StringComparison.Ordinal))
            {
                return Json(Game());
            }

            // league-v4: every stranger is Gold II, except one who is unranked.
            return path.EndsWith("/stranger-7", StringComparison.Ordinal)
                ? Json(Array.Empty<object>())
                : Json(new[]
                {
                    new { queueType = "RANKED_FLEX_SR", tier = "IRON", rank = "IV", leaguePoints = 1, wins = 1, losses = 1 },
                    new { queueType = "RANKED_SOLO_5x5", tier = "GOLD", rank = "II", leaguePoints = 42, wins = 30, losses = 20 },
                });
        });
        await using var factory = CreateFactory(riot);
        using var client = CreateClient(factory);

        var live = await client.GetFromJsonAsync<LiveGameReadModel>("/truemains/Subject-EUW1/live");

        live!.Status.Should().Be(LiveGameStatuses.InGame);
        var game = live.Game!;
        game.QueueId.Should().Be(420);
        game.StartedAtUtc.Should().Be(new DateTime(2026, 10, 20, 11, 50, 0, DateTimeKind.Utc));
        game.Teams.Select(t => t.TeamId).Should().Equal(100, 200);
        game.Teams[0].Bans.Should().Equal(64, 238);

        var blue = game.Teams[0].Participants;
        blue.Should().HaveCount(5);
        // The Smite carrier is the jungler, whatever the champion priors say.
        blue.Single(p => p.Spell1Id == Smite || p.Spell2Id == Smite).Position.Should().Be("JUNGLE");
        blue.Select(p => p.Position).Should().Equal("TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY");

        var subject = blue.Single(p => p.IsSubject);
        subject.GameName.Should().Be("Subject");
        subject.TagLine.Should().Be("EUW1");
        subject.IsTracked.Should().BeTrue();
        subject.Ranked!.Tier.Should().Be("MASTER");
        subject.Ranked.LeaguePoints.Should().Be(120);
        subject.KeystoneId.Should().Be(8005);
        subject.PrimaryStyleId.Should().Be(8000);
        subject.SecondaryStyleId.Should().Be(8400);

        var all = game.Teams.SelectMany(t => t.Participants).ToList();
        all.Single(p => p.GameName == "Tracked").Ranked!.Tier.Should().Be("DIAMOND");
        all.Single(p => p.GameName == "Stranger3").Ranked!.Should().BeEquivalentTo(new ProfileRankedReadModel
        {
            Tier = "GOLD",
            Division = "II",
            LeaguePoints = 42,
            Wins = 30,
            Losses = 20,
            WinRate = 0.6,
        });
        all.Single(p => p.GameName == "Stranger7").Ranked.Should().BeNull("an unranked player has no badge");
        all.Where(p => p.IsTracked).Should().HaveCount(2);

        // One spectator call, and league-v4 only for the eight players the database cannot rank.
        riot.Calls.Count(c => c.Contains("/spectator/", StringComparison.Ordinal)).Should().Be(1);
        riot.Calls.Where(c => c.Contains("/league/", StringComparison.Ordinal)).Should().HaveCount(8)
            .And.NotContain(c => c.EndsWith(SubjectPuuid, StringComparison.Ordinal) || c.EndsWith(TrackedPuuid, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARateLimitedRiotIsUnavailableAndPausesTheClient()
    {
        await SeedAsync();
        using var riot = new StubRiot(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
            return response;
        });
        var clock = new SteppingClock(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        await using var factory = CreateFactory(riot, clock);
        using var client = CreateClient(factory);

        var live = await client.GetFromJsonAsync<LiveGameReadModel>("/truemains/Subject-EUW1/live");
        live!.Status.Should().Be(LiveGameStatuses.Unavailable);

        // Past the refresh floor the answer is re-asked for, but the 429's Retry-After keeps
        // the client from reaching Riot again.
        clock.Advance(TimeSpan.FromSeconds(15));
        (await client.GetFromJsonAsync<LiveGameReadModel>("/truemains/Subject-EUW1/live?refresh=true"))!
            .Status.Should().Be(LiveGameStatuses.Unavailable);
        riot.Calls.Should().ContainSingle();
    }

    private async Task SeedAsync()
    {
        await using var db = _fixture.CreateDbContext();
        var subject = Account(SubjectPuuid, "Subject");
        var tracked = Account(TrackedPuuid, "Tracked");
        db.RiotAccounts.AddRange(subject, tracked);
        db.RankSnapshots.AddRange(
            Snapshot(subject, RankTier.Master, RankDivision.I, 120),
            Snapshot(tracked, RankTier.Diamond, RankDivision.II, 55));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A ranked game: the subject on blue mid, the tracked player on red top, eight strangers.
    /// No lane priors are seeded, so every champion is uniform and the Smite pin plus Riot's
    /// order are what the placement rests on — enough to check the order and the pin.
    /// </summary>
    private static object Game()
    {
        var participants = new List<object>
        {
            Participant("stranger-1", "Stranger1", 100, 266, Flash, Ignite),
            Participant("stranger-2", "Stranger2", 100, 64, Flash, Smite),
            Participant(SubjectPuuid, "Subject", 100, 103, Flash, Ignite),
            Participant("stranger-3", "Stranger3", 100, 222, Flash, 7),
            Participant("stranger-4", "Stranger4", 100, 412, Flash, Ignite),
            Participant(TrackedPuuid, "Tracked", 200, 86, Flash, 12),
            Participant("stranger-5", "Stranger5", 200, 121, Smite, Flash),
            Participant("stranger-6", "Stranger6", 200, 61, Flash, 12),
            Participant("stranger-7", "Stranger7", 200, 51, Flash, 7),
            Participant("stranger-8", "Stranger8", 200, 117, Flash, Ignite),
        };

        return new
        {
            gameId = 7_000_000_001L,
            gameMode = "CLASSIC",
            mapId = 11,
            gameQueueConfigId = 420,
            platformId = "EUW1",
            gameStartTime = new DateTimeOffset(2026, 10, 20, 11, 50, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            gameLength = 600,
            bannedChampions = new[]
            {
                new { championId = 238, teamId = 100, pickTurn = 2 },
                new { championId = 64, teamId = 100, pickTurn = 1 },
                new { championId = -1, teamId = 100, pickTurn = 3 },
                new { championId = 157, teamId = 200, pickTurn = 6 },
            },
            participants,
        };
    }

    private static object Participant(string puuid, string name, int teamId, int championId, int spell1, int spell2)
        => new
        {
            puuid,
            riotId = $"{name}#EUW1",
            teamId,
            championId,
            profileIconId = 1,
            spell1Id = spell1,
            spell2Id = spell2,
            bot = false,
            perks = new { perkIds = new[] { 8005, 9111, 9104, 8299, 8444, 8451 }, perkStyle = 8000, perkSubStyle = 8400 },
        };

    private static HttpResponseMessage Json(object body)
        => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    private static RiotAccount Account(string puuid, string gameName)
        => new()
        {
            Id = Guid.NewGuid(),
            Puuid = puuid,
            GameName = gameName,
            TagLine = "EUW1",
            PlatformId = "EUW1",
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            LastMatchIngestAtUtc = DateTime.UtcNow,
        };

    private static RankSnapshot Snapshot(RiotAccount account, RankTier tier, RankDivision division, int leaguePoints)
        => new()
        {
            Id = Guid.NewGuid(),
            RiotAccountId = account.Id,
            CapturedAtUtc = DateTime.UtcNow,
            Tier = tier,
            Division = division,
            LeaguePoints = leaguePoints,
            Wins = 10,
            Losses = 10,
        };

    private LiveFactory CreateFactory(StubRiot riot, TimeProvider? clock = null)
        => new(_fixture, riot) { TimeProvider = clock };

    private static HttpClient CreateClient(LiveFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private sealed class LiveFactory(PostgresFixture fixture, StubRiot riot)
        : TrueMainWebApplicationFactory<Program>(fixture, [new KeyValuePair<string, string?>("Riot:ApiKey", "RGAPI-test")])
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services
                .AddHttpClient<IRiotLiveClient, RiotLiveClient>()
                .ConfigurePrimaryHttpMessageHandler(() => riot));
        }
    }

    /// <summary>Riot, answered by <paramref name="respond"/>; every requested URL is recorded.</summary>
    private sealed class StubRiot(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _calls = new();

        public IReadOnlyList<string> Calls => [.. _calls];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.GetValues("X-Riot-Token").Should().ContainSingle().Which.Should().Be("RGAPI-test");
            _calls.Enqueue(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }

        protected override void Dispose(bool disposing)
        {
            // Shared across the factory's handler pipelines; nothing to release.
        }
    }

    private sealed class SteppingClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
