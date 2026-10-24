using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Entities;
using Data.Logging.Mongo;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using TrueMain.ReadModels.Ops;
using TrueMain.ReadModels.Personas;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The operator's management of known players (#2019): manual add and edit, unlink, link removal,
/// removal and takedown — each holding when the scrapers send the same batches again, and audited.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class PersonaEditsApiIntegrationTests : IAsyncLifetime
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;
    private readonly MongoFixture _mongo;

    public PersonaEditsApiIntegrationTests(PostgresFixture fixture, MongoFixture mongo)
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
    public async Task Create_AddsAManualPersona_WhoseNameTheSourcesNoLongerOverwrite()
    {
        await SeedTrackedAsync("puuid-caps", "G2 Caps", "1323");
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync("/ops/personas", new
        {
            slug = "caps",
            displayName = "Caps",
            links = new[] { new { kind = "twitch", url = "https://www.twitch.tv/caps/" } },
            accounts = new[] { new { gameName = "g2 caps", tagLine = "1323", platformId = "euw1" } },
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = (await response.Content.ReadFromJsonAsync<PersonaEditedResponse>())!;
        created.Slug.Should().Be("caps");
        created.Accounts.Single().Status.Should().Be("linked");

        var player = await PlayerAsync(client, "caps");
        player.DisplayName.Should().Be("Caps");
        player.Links.Single().Url.Should().Be("https://www.twitch.tv/caps");

        // A source sends the same slug under another name: the operator's name stays.
        (await BatchAsync(client, "caps", "leaguepedia", ("G2 Caps", "1323"))).Created.Should().BeFalse();
        (await PlayerAsync(client, "caps")).DisplayName.Should().Be("Caps");

        var detail = await DetailAsync(client, created.Id);
        detail.Source.Should().Be("Manual");
        detail.DisplayNameLocked.Should().BeTrue();
        detail.Accounts.Single().Source.Should().Be("Manual");

        (await client.PostAsJsonAsync("/ops/personas", new { slug = "caps", displayName = "Again" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync("/ops/personas", new { slug = "Not A Slug", displayName = "X" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/ops/personas", new
            {
                slug = "badlink", displayName = "X", links = new[] { new { kind = "myspace", url = "https://x.test" } },
            }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await AuditActionsAsync()).Should().Contain("create_persona");
    }

    [Fact]
    public async Task Rename_LocksTheName_AgainstTheNextBatch()
    {
        await SeedTrackedAsync("puuid-caps", "G2 Caps", "1323");
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "caps", "leaguepedia", ("G2 Caps", "1323"));
        var id = await IdAsync("caps");

        (await client.PatchAsJsonAsync($"/ops/personas/{id}", new { displayName = "Caps (G2)" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await BatchAsync(client, "caps", "leaguepedia", ("G2 Caps", "1323"));

        (await PlayerAsync(client, "caps")).DisplayName.Should().Be("Caps (G2)");
        (await client.PatchAsJsonAsync($"/ops/personas/{id}", new { displayName = "  " }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PatchAsJsonAsync($"/ops/personas/{Guid.NewGuid()}", new { displayName = "X" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AuditActionsAsync()).Should().Contain("rename_persona");
    }

    [Fact]
    public async Task Unlink_HoldsAgainstTheSource_AndAManualAddLiftsIt()
    {
        await SeedTrackedAsync("puuid-caps", "G2 Caps", "1323");
        await SeedTrackedAsync("puuid-alt", "Caps Alt", "EUW");
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "caps", "leaguepedia", ("G2 Caps", "1323"), ("Caps Alt", "EUW"));
        var id = await IdAsync("caps");
        var alt = (await DetailAsync(client, id)).Accounts.Single(a => a.GameName == "Caps Alt");

        (await client.DeleteAsync($"/ops/personas/{id}/accounts/{alt.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PlayerAsync(client, "caps")).Accounts.Should().ContainSingle().Which.GameName.Should().Be("G2 Caps");

        var again = await BatchAsync(client, "caps", "leaguepedia", ("G2 Caps", "1323"), ("caps alt", "euw"));
        again.Accounts.Single(a => a.GameName == "caps alt").Status.Should().Be("excluded");
        var detail = await DetailAsync(client, id);
        detail.Accounts.Should().ContainSingle();
        detail.Exclusions.Single().Reason.Should().Be("unlinked");

        var added = await client.PostAsJsonAsync($"/ops/personas/{id}/accounts", new { gameName = "Caps Alt", tagLine = "EUW", platformId = "EUW1" });
        added.StatusCode.Should().Be(HttpStatusCode.OK);
        (await added.Content.ReadFromJsonAsync<PersonaEditedResponse>())!.Accounts.Single().Status.Should().Be("linked");
        (await DetailAsync(client, id)).Exclusions.Should().BeEmpty();
        (await PlayerAsync(client, "caps")).Accounts.Should().HaveCount(2);

        (await client.DeleteAsync($"/ops/personas/{id}/accounts/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AuditActionsAsync()).Should().Contain(["unlink_persona_account", "add_persona_account"]);
    }

    [Fact]
    public async Task Unlink_SettlesTheOpenClaimsOnTheAccount_SoTheClaimantTakesItNextRun()
    {
        await SeedTrackedAsync("puuid-caps", "G2 Caps", "1323");
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "notcaps", "deeplol", ("G2 Caps", "1323"));
        await BatchAsync(client, "caps", "leaguepedia", ("G2 Caps", "1323"));
        var holder = await IdAsync("notcaps");
        (await ListAsync(client, "?view=conflicts")).Personas.Select(p => p.Slug).Should().BeEquivalentTo(["caps", "notcaps"]);
        (await DetailAsync(client, holder)).Conflicts.Single().Role.Should().Be("owner");

        var row = (await DetailAsync(client, holder)).Accounts.Single();
        (await client.DeleteAsync($"/ops/personas/{holder}/accounts/{row.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ListAsync(client, "?view=conflicts")).Total.Should().Be(0);

        (await BatchAsync(client, "caps", "leaguepedia", ("G2 Caps", "1323"))).Accounts.Single().Status.Should().Be("linked");
        (await BatchAsync(client, "notcaps", "deeplol", ("G2 Caps", "1323"))).Accounts.Single().Status.Should().Be("excluded");
    }

    [Fact]
    public async Task RemoveLink_HoldsAgainstTheSource_AndAManualAddLiftsIt()
    {
        await SeedTrackedAsync("puuid-caps", "G2 Caps", "1323");
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var link = new { kind = "youtube", url = "https://www.youtube.com/@caps" };
        await BatchAsync(client, "caps", "leaguepedia", [link], ("G2 Caps", "1323"));
        var id = await IdAsync("caps");
        var linkId = (await DetailAsync(client, id)).Links.Single().Id;

        (await client.DeleteAsync($"/ops/personas/{id}/links/{linkId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        await BatchAsync(client, "caps", "leaguepedia", [link], ("G2 Caps", "1323"));
        var detail = await DetailAsync(client, id);
        detail.Links.Should().BeEmpty();
        detail.RemovedLinks.Single().Url.Should().Be("https://www.youtube.com/@caps");

        (await client.PostAsJsonAsync($"/ops/personas/{id}/links", link)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync($"/ops/personas/{id}/links", link)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync($"/ops/personas/{id}/links", new { kind = "twitch", url = "ftp://x" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        detail = await DetailAsync(client, id);
        detail.Links.Single().Source.Should().Be("Manual");
        detail.RemovedLinks.Should().BeEmpty();
        (await AuditActionsAsync()).Should().Contain(["remove_persona_link", "add_persona_link"]);
    }

    [Fact]
    public async Task Remove_DropsThePage_AndTheSourcesCannotRecreateIt_ButTheAccountsStayFree()
    {
        await SeedTrackedAsync("puuid-caps", "G2 Caps", "1323");
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "wrong", "dpm", ("G2 Caps", "1323"));
        await BatchAsync(client, "caps", "leaguepedia", ("G2 Caps", "1323"));
        var id = await IdAsync("wrong");

        (await client.DeleteAsync($"/ops/personas/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/players/wrong")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ListAsync(client, "?view=conflicts")).Total.Should().Be(0);
        (await ListAsync(client, "?view=removed")).Personas.Single().Slug.Should().Be("wrong");
        (await ListAsync(client, "")).Personas.Select(p => p.Slug).Should().Equal("caps");

        var resent = await BatchAsync(client, "wrong", "dpm", ("G2 Caps", "1323"));
        resent.Removed.Should().BeTrue();
        resent.Accounts.Should().BeEmpty();
        (await client.GetAsync("/players/wrong")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Not a takedown: the account goes to the persona the other source lists.
        (await BatchAsync(client, "caps", "leaguepedia", ("G2 Caps", "1323"))).Accounts.Single().Status.Should().Be("linked");

        (await client.DeleteAsync($"/ops/personas/{id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PatchAsJsonAsync($"/ops/personas/{id}", new { displayName = "Back" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync("/ops/personas", new { slug = "wrong", displayName = "Back" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await AuditActionsAsync()).Should().Contain("remove_persona");
    }

    [Fact]
    public async Task Takedown_BarsTheAccountsFromEveryPersona()
    {
        await SeedTrackedAsync("puuid-caps", "G2 Caps", "1323");
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "caps", "leaguepedia", ("G2 Caps", "1323"), ("Caps Unseen", "EUW"));
        var id = await IdAsync("caps");

        (await client.DeleteAsync($"/ops/personas/{id}?takedown=true")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Another source lists the same person under another slug: neither account comes back.
        var other = await BatchAsync(client, "caps-dpm", "dpm", ("g2 caps", "1323"), ("Caps Unseen", "EUW"));
        other.Accounts.Should().OnlyContain(a => a.Status == "excluded");
        (await client.GetAsync("/players/caps-dpm")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var otherId = await IdAsync("caps-dpm");
        var manual = await client.PostAsJsonAsync($"/ops/personas/{otherId}/accounts", new { gameName = "G2 Caps", tagLine = "1323", platformId = "EUW1" });
        (await manual.Content.ReadFromJsonAsync<PersonaEditedResponse>())!.Accounts.Single().Status.Should().Be("excluded");
        (await DetailAsync(client, id)).Exclusions.Should().OnlyContain(e => e.Reason == "takedown").And.HaveCount(2);
    }

    [Fact]
    public async Task List_FiltersAndSearches_AndRejectsWhatItCannotRead()
    {
        await SeedTrackedAsync("puuid-caps", "G2 Caps", "1323");
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "caps", "leaguepedia", ("G2 Caps", "1323"), ("Caps Unseen", "EUW"));
        await BatchAsync(client, "faker", "lolpros", ("Hide on bush", "KR1"));

        var all = await ListAsync(client, "");
        all.Total.Should().Be(2);
        var caps = all.Personas.Single(p => p.Slug == "caps");
        caps.TrackedAccounts.Should().Be(1);
        caps.UnresolvedAccounts.Should().Be(1);
        (await ListAsync(client, "?view=unresolved")).Total.Should().Be(2);
        (await ListAsync(client, "?search=unseen")).Personas.Single().Slug.Should().Be("caps");
        (await ListAsync(client, "?source=LoLPros")).Personas.Single().Slug.Should().Be("faker");
        (await ListAsync(client, "?pageSize=1&page=2")).Personas.Should().ContainSingle();

        (await client.GetAsync("/ops/personas?view=maybe")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/ops/personas?source=nowhere")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync($"/ops/personas/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        (await anonymous.GetAsync("/ops/personas")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"/ops/personas/{caps.Id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task SeedTrackedAsync(string puuid, string gameName, string tagLine, string platformId = "EUW1")
    {
        await using var db = _fixture.CreateDbContext();
        db.RiotAccounts.Add(new RiotAccount
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
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> IdAsync(string slug)
    {
        await using var db = _fixture.CreateDbContext();
        return await db.Personas.AsNoTracking().Where(p => p.Slug == slug).Select(p => p.Id).SingleAsync();
    }

    private async Task<List<string>> AuditActionsAsync()
    {
        var events = await _mongo.GetCollection<AuditEventDocument>(MongoFixture.AuditCollection)
            .Find(FilterDefinition<AuditEventDocument>.Empty)
            .ToListAsync();
        events.Should().OnlyContain(e => e.TargetType == "Persona" && e.Metadata!.ContainsKey("slug"));
        return events.Select(e => e.Action).ToList();
    }

    private static Task<PersonaUpsertResult> BatchAsync(
        HttpClient client, string slug, string source, params (string GameName, string TagLine)[] accounts) =>
        BatchAsync(client, slug, source, [], accounts);

    private static async Task<PersonaUpsertResult> BatchAsync(
        HttpClient client, string slug, string source, object[] links, params (string GameName, string TagLine)[] accounts)
    {
        var response = await client.PostAsJsonAsync("/ops/personas/batch", new
        {
            personas = new[]
            {
                new
                {
                    slug,
                    displayName = slug,
                    source,
                    seedMissing = false,
                    links,
                    accounts = accounts.Select(a => new
                    {
                        gameName = a.GameName,
                        tagLine = a.TagLine,
                        platformId = a.TagLine == "KR1" ? "KR" : "EUW1",
                    }),
                },
            },
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PersonaBatchResponse>())!.Personas.Single();
    }

    private static async Task<PersonasPageReadModel> ListAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<PersonasPageReadModel>($"/ops/personas{query}"))!;

    private static async Task<PersonaAdminDetailReadModel> DetailAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<PersonaAdminDetailReadModel>($"/ops/personas/{id}"))!;

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
