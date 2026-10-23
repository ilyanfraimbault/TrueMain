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
/// The persona conflict queue (#2253): the intake records an account two personas claim, the admin
/// settles it (move / keep / detach / merge), and the decision holds when the scrapers send the same
/// batches again.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class PersonaConflictsApiIntegrationTests : IAsyncLifetime
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;
    private readonly MongoFixture _mongo;

    public PersonaConflictsApiIntegrationTests(PostgresFixture fixture, MongoFixture mongo)
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
    public async Task Batch_RecordsTheConflict_WithBothPersonas_AndARepeatOnlyCountsIt()
    {
        await SeedTrackedAsync("puuid-caps", "G2 Caps", "1323");
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        await BatchAsync(client, "caps", "leaguepedia", ("G2 Caps", "1323"), ("Caps Alt", "EUW"));
        var claim = await BatchAsync(client, "capsdpm", "dpm", ("g2 caps", "1323"));
        claim.Accounts.Single().Status.Should().Be("conflict");
        await BatchAsync(client, "capsdpm", "dpm", ("g2 caps", "1323"));

        var page = await GetConflictsAsync(client, "?status=Open");
        page.Open.Should().Be(1);
        var conflict = page.Conflicts.Single();
        conflict.Claimant.Slug.Should().Be("capsdpm");
        conflict.Owner.Slug.Should().Be("caps");
        conflict.Owner.AccountCount.Should().Be(2);
        conflict.Source.Should().Be("Dpm");
        conflict.TimesSeen.Should().Be(2);
        conflict.Account.CurrentGameName.Should().Be("G2 Caps");
        conflict.Account.RiotAccountId.Should().NotBeNull();

        var detail = await client.GetFromJsonAsync<PersonaConflictDetailReadModel>($"/ops/personas/conflicts/{conflict.Id}");
        detail!.Owner.Accounts.Should().ContainSingle(a => a.Disputed).Which.GameName.Should().Be("G2 Caps");
        detail.Claimant.Accounts.Should().BeEmpty();

        (await GetConflictsAsync(client, "?search=CAPSD")).Total.Should().Be(1);
        (await GetConflictsAsync(client, "?status=Resolved")).Total.Should().Be(0);
        (await client.GetAsync("/ops/personas/conflicts?status=Maybe")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Move_GivesTheAccountToTheClaimant_AndTheFormerHolderCannotTakeItBack()
    {
        await SeedTrackedAsync("puuid-caps", "G2 Caps", "1323");
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "caps", "leaguepedia", ("G2 Caps", "1323"));
        await BatchAsync(client, "capsdpm", "dpm", ("G2 Caps", "1323"));
        var id = (await GetConflictsAsync(client, "")).Conflicts.Single().Id;

        var resolved = await ResolveAsync(client, id, new { action = "move" });
        resolved.Resolution.Should().Be("Moved");
        resolved.HolderSlug.Should().Be("capsdpm");

        (await PlayerAsync(client, "capsdpm")).Accounts.Should().ContainSingle();
        // No tracked account left: no page to show (the existing rule of GET /players/{slug}).
        (await client.GetAsync("/players/caps")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Next week: both sources list it again. Nothing moves and nothing reopens.
        (await BatchAsync(client, "caps", "leaguepedia", ("G2 Caps", "1323"))).Accounts.Single().Status.Should().Be("excluded");
        (await BatchAsync(client, "capsdpm", "dpm", ("G2 Caps", "1323"))).Accounts.Single().Status.Should().Be("linked");
        (await GetConflictsAsync(client, "?status=Open")).Total.Should().Be(0);
        await AssertOneRowPerAccountAsync();

        (await client.PostAsJsonAsync($"/ops/personas/conflicts/{id}/resolve", new { action = "keep" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Keep_LeavesTheAccount_AndTheClaimIsIgnoredFromNowOn()
    {
        await SeedTrackedAsync("puuid-caps", "G2 Caps", "1323");
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "caps", "leaguepedia", ("G2 Caps", "1323"));
        await BatchAsync(client, "notcaps", "deeplol", ("g2 caps", "1323"), ("Not Caps", "EUW"));
        var id = (await GetConflictsAsync(client, "")).Conflicts.Single().Id;

        (await ResolveAsync(client, id, new { action = "keep" })).HolderSlug.Should().Be("caps");

        (await BatchAsync(client, "notcaps", "deeplol", ("g2 caps", "1323"))).Accounts.Single().Status.Should().Be("excluded");
        (await PlayerAsync(client, "caps")).Accounts.Should().ContainSingle();
        var page = await GetConflictsAsync(client, "");
        page.Open.Should().Be(0);
        page.Conflicts.Single().Resolution.Should().Be("Kept");
    }

    [Fact]
    public async Task Detach_TakesTheAccountFromBoth_AndBarsBoth()
    {
        await SeedTrackedAsync("puuid-shared", "Shared Acc", "EUW");
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "first", "leaguepedia", ("Shared Acc", "EUW"));
        await BatchAsync(client, "second", "dpm", ("Shared Acc", "EUW"));
        var id = (await GetConflictsAsync(client, "")).Conflicts.Single().Id;

        (await ResolveAsync(client, id, new { action = "detach" })).HolderSlug.Should().BeNull();

        (await BatchAsync(client, "first", "leaguepedia", ("Shared Acc", "EUW"))).Accounts.Single().Status.Should().Be("excluded");
        (await BatchAsync(client, "second", "dpm", ("Shared Acc", "EUW"))).Accounts.Single().Status.Should().Be("excluded");
        await using var db = _fixture.CreateDbContext();
        (await db.PersonaAccounts.CountAsync()).Should().Be(0);
        (await db.PersonaAccountConflicts.CountAsync(c => c.Status == PersonaConflictStatus.Open)).Should().Be(0);
    }

    [Fact]
    public async Task Merge_UnitesThePersonas_RedirectsTheOldSlug_AndTheScraperNeverRecreatesIt()
    {
        await SeedTrackedAsync("puuid-peng", "twtv Peng04", "NMIXX");
        await SeedTrackedAsync("puuid-peng2", "Peng Smurf", "EUW");
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "peng", "leaguepedia", ("twtv Peng04", "NMIXX"));
        await BatchAsync(
            client, "peng-dpm", "dpm",
            links: [new { kind = "twitch", url = "https://www.twitch.tv/peng" }],
            ("twtv Peng04", "NMIXX"), ("Peng Smurf", "EUW"));
        var id = (await GetConflictsAsync(client, "")).Conflicts.Single().Id;

        var resolved = await ResolveAsync(client, id, new { action = "merge", keep = "owner" });
        resolved.HolderSlug.Should().Be("peng");
        resolved.MergedSlug.Should().Be("peng-dpm");

        var page = await PlayerAsync(client, "peng-dpm");
        page.Slug.Should().Be("peng"); // the web page redirects on the slug it gets back
        page.Accounts.Should().HaveCount(2);
        page.Links.Should().ContainSingle(l => l.Kind == "twitch");

        var again = await BatchAsync(client, "peng-dpm", "dpm", ("twtv Peng04", "NMIXX"), ("Peng Smurf", "EUW"), ("Peng New", "EUW"));
        again.MergedInto.Should().Be("peng");
        again.Accounts.Select(a => a.Status).Should().Equal("linked", "linked", "seeded");
        await using var db = _fixture.CreateDbContext();
        (await db.Personas.CountAsync(p => p.MergedIntoPersonaId == null)).Should().Be(1);
        (await db.PersonaAccounts.CountAsync(a => a.Persona!.Slug == "peng")).Should().Be(3);
        (await db.PersonaAccountConflicts.CountAsync(c => c.Status == PersonaConflictStatus.Open)).Should().Be(0);
        var directory = await client.GetFromJsonAsync<PersonaDirectoryResponse>("/players");
        directory!.Players.Select(p => p.Slug).Should().Equal("peng");
        await AssertOneRowPerAccountAsync();
    }

    [Fact]
    public async Task UntrackedRiotIds_ConflictByName_AndMoveRespectsTheRiotIdIndex()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "holder", "leaguepedia", ("Untracked One", "KR1"));
        (await BatchAsync(client, "claimer", "dpm", ("untracked one", "kr1"))).Accounts.Single().Status.Should().Be("conflict");
        var conflict = (await GetConflictsAsync(client, "")).Conflicts.Single();
        conflict.Account.RiotAccountId.Should().BeNull();

        await ResolveAsync(client, conflict.Id, new { action = "move" });

        await using var db = _fixture.CreateDbContext();
        var row = await db.PersonaAccounts.Include(a => a.Persona).SingleAsync();
        row.Persona!.Slug.Should().Be("claimer");
        row.Source.Should().Be(PersonaSource.Manual);
        (await BatchAsync(client, "holder", "leaguepedia", ("Untracked One", "KR1"))).Accounts.Single().Status.Should().Be("excluded");
    }

    [Fact]
    public async Task Resolve_RejectsBadRequests_AndNeedsTheOpsKey()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "holder", "leaguepedia", ("Someone", "EUW"));
        await BatchAsync(client, "claimer", "dpm", ("Someone", "EUW"));
        var id = (await GetConflictsAsync(client, "")).Conflicts.Single().Id;

        (await client.PostAsJsonAsync($"/ops/personas/conflicts/{id}/resolve", new { action = "merge" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"/ops/personas/conflicts/{id}/resolve", new { action = "delete" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"/ops/personas/conflicts/{Guid.NewGuid()}/resolve", new { action = "keep" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/ops/personas/conflicts/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        (await anonymous.GetAsync("/ops/personas/conflicts")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"/ops/personas/conflicts/{id}/resolve", new { action = "keep" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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

    /// <summary>Both unique rules of persona_accounts: one row per tracked account, one per Riot ID.</summary>
    private async Task AssertOneRowPerAccountAsync()
    {
        await using var db = _fixture.CreateDbContext();
        var rows = await db.PersonaAccounts.AsNoTracking().ToListAsync();
        rows.Where(r => r.RiotAccountId is not null).GroupBy(r => r.RiotAccountId).Should().OnlyContain(g => g.Count() == 1);
        rows.GroupBy(r => (r.PlatformId, r.GameName.ToLowerInvariant(), r.TagLine.ToLowerInvariant()))
            .Should().OnlyContain(g => g.Count() == 1);
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
                    seedMissing = true,
                    links,
                    accounts = accounts.Select(a => new { gameName = a.GameName, tagLine = a.TagLine, platformId = "EUW1" }),
                },
            },
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PersonaBatchResponse>())!.Personas.Single();
    }

    private static async Task<PersonaConflictsReadModel> GetConflictsAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<PersonaConflictsReadModel>($"/ops/personas/conflicts{query}"))!;

    private static async Task<PersonaConflictResolvedResponse> ResolveAsync(HttpClient client, Guid id, object body)
    {
        var response = await client.PostAsJsonAsync($"/ops/personas/conflicts/{id}/resolve", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PersonaConflictResolvedResponse>())!;
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
