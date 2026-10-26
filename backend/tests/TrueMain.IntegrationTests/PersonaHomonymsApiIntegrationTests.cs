using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;
using TrueMain.ReadModels.Personas;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Known players sharing a name (#2271): a conflict between two of them is merged without the
/// operator, and the homonyms queue lists the ones with no account in common, to merge or to mark
/// as different people.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class PersonaHomonymsApiIntegrationTests : IAsyncLifetime
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;
    private readonly MongoFixture _mongo;

    public PersonaHomonymsApiIntegrationTests(PostgresFixture fixture, MongoFixture mongo)
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
    public async Task Batch_MergesASameNameConflict_IntoTheOlderPersona()
    {
        await SeedTrackedAsync("puuid-rsk", "RekSai King", "EUW");
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "reksaiking", "RekSaiKing", "deeplol", ("RekSai King", "EUW"));

        var claim = await BatchAsync(client, "reksaiking-reksaiking", "reksaiking", "dpm", ("reksai king", "euw"), ("RSK Smurf", "EUW"));

        claim.Accounts.First().Status.Should().Be("conflict");
        var page = await PlayerAsync(client, "reksaiking-reksaiking");
        page.Slug.Should().Be("reksaiking");
        page.DisplayName.Should().Be("RekSaiKing");
        await using var db = _fixture.CreateDbContext();
        (await db.Personas.CountAsync(p => p.MergedIntoPersonaId == null)).Should().Be(1);
        (await db.PersonaAccounts.CountAsync(a => a.Persona!.Slug == "reksaiking")).Should().Be(2);
        var conflict = await db.PersonaAccountConflicts.SingleAsync();
        conflict.Status.Should().Be(PersonaConflictStatus.Resolved);
        conflict.Resolution.Should().Be(PersonaConflictResolution.Merged);

        var again = await BatchAsync(client, "reksaiking-reksaiking", "reksaiking", "dpm", ("reksai king", "euw"));
        again.MergedInto.Should().Be("reksaiking");
        again.Accounts.Single().Status.Should().Be("linked");
    }

    [Fact]
    public async Task AutoMerge_SweepsTheBacklog_AndLeavesConflictsBetweenDifferentNames()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "caps", "Caps", "leaguepedia", ("G2 Caps", "1323"));
        await BatchAsync(client, "capsdpm", "Caps Dpm", "dpm", ("G2 Caps", "1323"));
        await BatchAsync(client, "holder", "Holder", "leaguepedia", ("Someone", "EUW"));
        await BatchAsync(client, "claimer", "Claimer", "dpm", ("Someone", "EUW"));
        (await GetAsync<PersonaConflictsReadModel>(client, "/ops/personas/conflicts")).Open.Should().Be(2);

        // An operator rename does not sweep: the backlog waits for the next batch or this button.
        var capsDpm = await PersonaIdAsync("capsdpm");
        (await client.PatchAsJsonAsync($"/ops/personas/{capsDpm}", new { displayName = "C.A.P.S" })).EnsureSuccessStatusCode();
        (await GetAsync<PersonaConflictsReadModel>(client, "/ops/personas/conflicts")).Open.Should().Be(2);

        var swept = await PostAsync<PersonaAutoMergeResponse>(client, "/ops/personas/conflicts/auto-merge", new { });

        swept.Merged.Should().Be(1);
        swept.Skipped.Should().Be(0);
        var open = await GetAsync<PersonaConflictsReadModel>(client, "/ops/personas/conflicts?status=Open");
        open.Conflicts.Single().Claimant.Slug.Should().Be("claimer");
        await using var db = _fixture.CreateDbContext();
        (await db.Personas.SingleAsync(p => p.Slug == "capsdpm")).MergedIntoPersonaId.Should().Be(await PersonaIdAsync("caps"));
        (await PostAsync<PersonaAutoMergeResponse>(client, "/ops/personas/conflicts/auto-merge", new { })).Merged.Should().Be(0);
    }

    [Fact]
    public async Task Homonyms_ListsSameNamePlayers_UntilMergedOrMarkedDifferent()
    {
        await SeedTrackedAsync("puuid-rain-2", "Rain Two", "EUW");
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "rain", "Rain", "leaguepedia", ("Rain One", "EUW"));
        await BatchAsync(client, "rain-dpm", "rain", "dpm", ("Rain Two", "EUW"));
        await BatchAsync(client, "rain-helena-ruiz", "R.A.I.N", "leaguepedia", ("Helena", "EUW"));
        await BatchAsync(client, "other", "Other", "leaguepedia", ("Other", "EUW"));

        var groups = await GetAsync<PersonaHomonymsReadModel>(client, "/ops/personas/homonyms");
        groups.Total.Should().Be(1);
        var group = groups.Groups.Single();
        group.Key.Should().Be("rain");
        group.Personas.Select(p => p.Slug).Should().Equal("rain", "rain-dpm", "rain-helena-ruiz");
        group.Personas[1].Accounts.Single().GameName.Should().Be("Rain Two");
        (await GetAsync<PersonaHomonymsReadModel>(client, "/ops/personas/homonyms?search=helena")).Total.Should().Be(1);
        (await GetAsync<PersonaHomonymsReadModel>(client, "/ops/personas/homonyms?search=zzz")).Total.Should().Be(0);

        var rain = group.Personas[0].Id;
        var rainDpm = group.Personas[1].Id;
        var helena = group.Personas[2].Id;
        (await PostAsync<PersonaHomonymDistinctResponse>(client, "/ops/personas/homonyms/distinct", new { personaIds = new[] { rain, helena } }))
            .Marked.Should().Be(1);
        var stillThere = (await GetAsync<PersonaHomonymsReadModel>(client, "/ops/personas/homonyms")).Groups.Single();
        stillThere.DistinctPairs.Should().ContainSingle();

        var merged = await PostAsync<PersonaHomonymMergeResponse>(
            client, "/ops/personas/homonyms/merge", new { keepId = rain, mergeIds = new[] { rainDpm } });

        merged.KeptSlug.Should().Be("rain");
        merged.MergedSlugs.Should().Equal("rain-dpm");
        (await GetAsync<PersonaHomonymsReadModel>(client, "/ops/personas/homonyms")).Total.Should().Be(0);
        (await PlayerAsync(client, "rain-dpm")).Slug.Should().Be("rain");
        (await BatchAsync(client, "rain-dpm", "rain", "dpm", ("Rain Two", "EUW"))).MergedInto.Should().Be("rain");

        // A new persona with the name brings the group back: the marks cover the pairs they name.
        await BatchAsync(client, "rain-new", "RAIN", "trackingthepros", ("Rain New", "EUW"));
        (await GetAsync<PersonaHomonymsReadModel>(client, "/ops/personas/homonyms")).Groups.Single().Personas.Should().HaveCount(3);
    }

    [Fact]
    public async Task Homonyms_ListTheGroupsSharingALinkFirst()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "alpha", "Alpha", "leaguepedia", ("Alpha One", "EUW"));
        await BatchAsync(client, "alpha-dpm", "alpha", "dpm", ("Alpha Two", "EUW"));
        // A shared reference page, not a social channel: that one would merge them (#2286).
        await BatchAsync(client, "yongi", "Yongi", "deeplol", [new { kind = "leaguepedia", url = "https://lol.fandom.com/wiki/Yongi" }], ("Yongi One", "KR"));
        await BatchAsync(client, "yongi-dpm", "yongi", "dpm", [new { kind = "leaguepedia", url = "https://lol.fandom.com/wiki/Yongi/" }], ("Yongi Two", "KR"));

        var groups = await GetAsync<PersonaHomonymsReadModel>(client, "/ops/personas/homonyms");

        groups.Groups.Select(g => g.Key).Should().Equal("yongi", "alpha");
        groups.Groups[0].SharedLinks.Should().Equal("lol.fandom.com/wiki/yongi");
        groups.Groups[1].SharedLinks.Should().BeEmpty();
    }

    [Fact]
    public async Task Batch_MergesSameNamePlayersSharingASocialChannel_IntoTheOlder()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "irelking", "IRELKING", "deeplol", [Chzzk("https://chzzk.naver.com/993db7ed")], ("aileri", "KR1"));
        await BatchAsync(client, "unrelated", "Unrelated", "deeplol", [Chzzk("https://chzzk.naver.com/other")], ("Unrelated", "KR1"));

        // Another source, another account, the same channel spelled its own way: one person (#2286).
        await BatchAsync(client, "irelking-irelking", "IRELKING", "dpm", [Chzzk("http://www.chzzk.naver.com/993DB7ED/")], ("Kasumi", "1899"));

        (await GetAsync<PersonaChannelConflictsReadModel>(client, "/ops/personas/channel-conflicts")).Total.Should().Be(0);
        await using var db = _fixture.CreateDbContext();
        (await db.Personas.SingleAsync(p => p.Slug == "irelking-irelking")).MergedIntoPersonaId.Should().Be(await PersonaIdAsync("irelking"));
        (await db.PersonaAccounts.CountAsync(a => a.Persona!.Slug == "irelking")).Should().Be(2);
        (await db.PersonaLinks.CountAsync(l => l.Persona!.Slug == "irelking")).Should().Be(1);
    }

    [Fact]
    public async Task ChannelConflicts_ListDifferentNamesSharingAChannel_UntilMarkedDifferent()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "faker", "Faker", "leaguepedia", [Twitch("https://www.twitch.tv/faker")], ("Hide on bush", "KR1"));
        await BatchAsync(client, "unrelated", "Unrelated", "dpm", [Twitch("https://www.twitch.tv/someone")], ("Unrelated", "KR1"));
        await BatchAsync(
            client,
            "rosen",
            "Rosén",
            "deeplol",
            [Twitch("https://twitch.tv/Faker"), new { kind = "youtube", url = "https://www.youtube.com/@Ros%C3%A9n" }],
            ("Rosen", "KR1"));
        await BatchAsync(client, "rosen-dpm", "Rosen Dpm", "dpm", [new { kind = "youtube", url = "https://youtube.com/@Rosén" }], ("Rosen Two", "KR1"));

        var conflicts = await GetAsync<PersonaChannelConflictsReadModel>(client, "/ops/personas/channel-conflicts");

        conflicts.Total.Should().Be(2);
        conflicts.Conflicts.Select(c => (c.Older.Slug, c.Newer.Slug)).Should().BeEquivalentTo([("faker", "rosen"), ("rosen", "rosen-dpm")]);
        conflicts.Conflicts.Single(c => c.Older.Slug == "faker").SharedChannels.Should().ContainSingle(c => c.Kind == "twitch");
        conflicts.Conflicts.Single(c => c.Older.Slug == "faker").Newer.Accounts.Single().GameName.Should().Be("Rosen");
        (await GetAsync<PersonaChannelConflictsReadModel>(client, "/ops/personas/channel-conflicts?search=dpm")).Total.Should().Be(1);
        var directory = await GetAsync<PersonasPageReadModel>(client, "/ops/personas?view=Conflicts");
        directory.Personas.Select(p => (p.Slug, p.OpenConflicts)).Should().BeEquivalentTo([("faker", 1), ("rosen", 2), ("rosen-dpm", 1)]);

        var faker = await PersonaIdAsync("faker");
        var rosen = await PersonaIdAsync("rosen");
        await PostAsync<PersonaHomonymDistinctResponse>(client, "/ops/personas/homonyms/distinct", new { personaIds = new[] { faker, rosen } });
        await PostAsync<PersonaHomonymMergeResponse>(
            client, "/ops/personas/homonyms/merge", new { keepId = rosen, mergeIds = new[] { await PersonaIdAsync("rosen-dpm") } });

        (await GetAsync<PersonaChannelConflictsReadModel>(client, "/ops/personas/channel-conflicts")).Total.Should().Be(0);
        (await GetAsync<PersonasPageReadModel>(client, "/ops/personas?view=Conflicts")).Total.Should().Be(0);
    }

    [Fact]
    public async Task AutoMerge_LeavesASameNameChannelGroup_HoldingAPairMarkedDifferent()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "kim-a", "Kim", "leaguepedia", ("Kim A", "KR1"));
        await BatchAsync(client, "kim-b", "Kim", "dpm", ("Kim B", "KR1"));
        await BatchAsync(client, "kim-c", "KIM", "deeplol", ("Kim C", "KR1"));
        var a = await PersonaIdAsync("kim-a");
        var b = await PersonaIdAsync("kim-b");
        var c = await PersonaIdAsync("kim-c");
        await PostAsync<PersonaHomonymDistinctResponse>(client, "/ops/personas/homonyms/distinct", new { personaIds = new[] { a, b } });

        // kim-c shares a channel with each: merging the three would undo the operator's mark.
        await using (var db = _fixture.CreateDbContext())
        {
            db.PersonaLinks.AddRange(
                new PersonaLink { PersonaId = a, Kind = PersonaLinkKind.Twitch, Url = "https://twitch.tv/kim", Source = PersonaSource.Leaguepedia },
                new PersonaLink { PersonaId = c, Kind = PersonaLinkKind.Twitch, Url = "https://twitch.tv/kim", Source = PersonaSource.DeepLol },
                new PersonaLink { PersonaId = c, Kind = PersonaLinkKind.Chzzk, Url = "https://chzzk.naver.com/kim", Source = PersonaSource.DeepLol },
                new PersonaLink { PersonaId = b, Kind = PersonaLinkKind.Chzzk, Url = "https://chzzk.naver.com/kim", Source = PersonaSource.Dpm });
            await db.SaveChangesAsync();
        }

        var swept = await PostAsync<PersonaAutoMergeResponse>(client, "/ops/personas/conflicts/auto-merge", new { });

        swept.MergedByChannel.Should().Be(0);
        swept.Skipped.Should().Be(1);
        (await GetAsync<PersonaChannelConflictsReadModel>(client, "/ops/personas/channel-conflicts")).Total.Should().Be(2);
    }

    [Fact]
    public async Task Merge_CarriesTheMergedPersonasMarks_ToTheKeptOne()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "zed-a", "Zed", "leaguepedia", ("Zed A", "EUW"));
        await BatchAsync(client, "zed-b", "zed", "dpm", ("Zed B", "EUW"));
        await BatchAsync(client, "zed-c", "ZED", "deeplol", ("Zed C", "EUW"));
        var a = await PersonaIdAsync("zed-a");
        var b = await PersonaIdAsync("zed-b");
        var c = await PersonaIdAsync("zed-c");
        await PostAsync<PersonaHomonymDistinctResponse>(client, "/ops/personas/homonyms/distinct", new { personaIds = new[] { b, c } });

        await PostAsync<PersonaHomonymMergeResponse>(client, "/ops/personas/homonyms/merge", new { keepId = a, mergeIds = new[] { b } });

        // b was different from c, so a is too: the group of a and c is settled.
        (await GetAsync<PersonaHomonymsReadModel>(client, "/ops/personas/homonyms")).Total.Should().Be(0);
        await using var db = _fixture.CreateDbContext();
        (await db.PersonaDistinctPairs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task HomonymActions_RejectBadRequests_AndNeedTheOpsKey()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "one", "Same", "leaguepedia", ("One", "EUW"));
        await BatchAsync(client, "two", "Same", "dpm", ("Two", "EUW"));
        var one = await PersonaIdAsync("one");
        var two = await PersonaIdAsync("two");

        (await client.PostAsJsonAsync("/ops/personas/homonyms/merge", new { keepId = one, mergeIds = new[] { one } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/ops/personas/homonyms/distinct", new { personaIds = new[] { one } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/ops/personas/homonyms/merge", new { keepId = one, mergeIds = new[] { Guid.NewGuid() } }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        await PostAsync<PersonaHomonymMergeResponse>(client, "/ops/personas/homonyms/merge", new { keepId = one, mergeIds = new[] { two } });
        (await client.PostAsJsonAsync("/ops/personas/homonyms/distinct", new { personaIds = new[] { one, two } }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        (await anonymous.GetAsync("/ops/personas/homonyms")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/ops/personas/conflicts/auto-merge", new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static object Chzzk(string url) => new { kind = "chzzk", url };

    private static object Twitch(string url) => new { kind = "twitch", url };

    private async Task SeedTrackedAsync(string puuid, string gameName, string tagLine)
    {
        await using var db = _fixture.CreateDbContext();
        db.RiotAccounts.Add(new RiotAccount
        {
            Id = Guid.NewGuid(),
            Puuid = puuid,
            GameName = gameName,
            TagLine = tagLine,
            PlatformId = "EUW1",
            ProfileIconId = 1,
            SummonerLevel = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            LastMatchIngestAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> PersonaIdAsync(string slug)
    {
        await using var db = _fixture.CreateDbContext();
        return await db.Personas.Where(p => p.Slug == slug).Select(p => p.Id).SingleAsync();
    }

    private static Task<PersonaUpsertResult> BatchAsync(
        HttpClient client, string slug, string displayName, string source, params (string GameName, string TagLine)[] accounts) =>
        BatchAsync(client, slug, displayName, source, [], accounts);

    private static async Task<PersonaUpsertResult> BatchAsync(
        HttpClient client, string slug, string displayName, string source, object[] links, params (string GameName, string TagLine)[] accounts)
    {
        var response = await client.PostAsJsonAsync("/ops/personas/batch", new
        {
            personas = new[]
            {
                new
                {
                    slug,
                    displayName,
                    source,
                    seedMissing = false,
                    links,
                    accounts = accounts.Select(a => new { gameName = a.GameName, tagLine = a.TagLine, platformId = "EUW1" }),
                },
            },
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PersonaBatchResponse>())!.Personas.Single();
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string url) => (await client.GetFromJsonAsync<T>(url))!;

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<PersonaReadModel> PlayerAsync(HttpClient client, string slug) =>
        (await client.GetFromJsonAsync<PersonaReadModel>($"/players/{slug}"))!;

    private ApiWebApplicationFactory CreateFactory() => new(_fixture, _mongo);

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }

    private sealed class ApiWebApplicationFactory(PostgresFixture fixture, MongoFixture mongo)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MongoLogging:ConnectionString", mongo.ConnectionString),
                new KeyValuePair<string, string?>("MongoLogging:Database", MongoFixture.DatabaseName),
                new KeyValuePair<string, string?>("MongoLogging:MinimumLevel", "None"),
            ]);
}
