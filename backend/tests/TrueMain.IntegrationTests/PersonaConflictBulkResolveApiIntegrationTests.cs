using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Entities;
using Data.Logging.Mongo;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using TrueMain.ReadModels.Ops;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The bulk resolution of the most certain conflicts (#2295): the preview at a score floor, then the
/// merge of exactly the previewed ids, each re-checked and audited like a single resolution.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class PersonaConflictBulkResolveApiIntegrationTests : IAsyncLifetime
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;
    private readonly MongoFixture _mongo;

    public PersonaConflictBulkResolveApiIntegrationTests(PostgresFixture fixture, MongoFixture mongo)
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
    public async Task Preview_ListsTheConflictsAtTheFloor_WithoutADifferingChannel_KeepingTheOlder()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await SeedConflictsAsync(client);

        var at20 = await PreviewAsync(client, 20);
        at20.Eligible.Should().Be(1);
        at20.Limit.Should().Be(100);
        var two = at20.Conflicts.Single();
        two.Conflict.Claimant.Slug.Should().Be("claimer-two");
        two.Conflict.Evidence.Score.Should().Be(20);
        two.Keep.Should().Be("owner");
        two.KeptSlug.Should().Be("holder-two");
        two.MergedSlug.Should().Be("claimer-two");

        // The one-channel conflict joins at 10; the one with a differing channel never does, nor the
        // one sharing only reference pages, whatever its score.
        var at10 = await PreviewAsync(client, 10);
        at10.Conflicts.Select(c => c.Conflict.Claimant.Slug).Should().Equal("claimer-two", "claimer-one");

        (await client.GetAsync("/ops/personas/conflicts/bulk-resolve/preview?minScore=9")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/ops/personas/conflicts/bulk-resolve/preview")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Resolve_MergesExactlyThePreviewedIds_AndAuditsEachOne()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await SeedConflictsAsync(client);
        // The claimant of the one-channel conflict is the older of its two: the merge keeps it.
        await using (var db = _fixture.CreateDbContext())
        {
            await db.Personas.Where(p => p.Slug == "claimer-one")
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAtUtc, DateTime.UtcNow.AddYears(-1)));
        }

        var preview = await PreviewAsync(client, 10);
        preview.Conflicts.Single(c => c.Conflict.Claimant.Slug == "claimer-one").KeptSlug.Should().Be("claimer-one");
        var ids = preview.Conflicts.Select(c => c.Conflict.Id).ToList();

        var result = await BulkAsync(client, ids, 10);

        result.Resolved.Should().Be(2);
        result.Skipped.Should().Be(0);
        result.Outcomes.Select(o => o.Id).Should().Equal(ids);
        result.Outcomes.Should().OnlyContain(o => o.Status == "resolved" && o.Resolution!.Resolution == "Merged");
        result.Outcomes.Select(o => (o.Resolution!.HolderSlug, o.Resolution.MergedSlug))
            .Should().Equal(("holder-two", "claimer-two"), ("claimer-one", "holder-one"));

        await using var check = _fixture.CreateDbContext();
        var open = await check.PersonaAccountConflicts.AsNoTracking()
            .Where(c => c.Status == PersonaConflictStatus.Open)
            .Select(c => c.Claimant!.Slug)
            .ToListAsync();
        open.Should().BeEquivalentTo("claimer-diff", "claimer-refs");

        var audited = await _mongo.GetCollection<AuditEventDocument>(MongoFixture.AuditCollection)
            .Find(e => e.Action == AuditActions.ResolvePersonaConflict)
            .ToListAsync();
        audited.Select(e => e.TargetId).Should().BeEquivalentTo(ids.Select(id => id.ToString()));
        audited.Should().OnlyContain(e => e.Actor == AuditActors.Operator && e.Metadata!["resolution"] == "Merged");
    }

    [Fact]
    public async Task Resolve_SkipsWhatChangedSinceThePreview_WithItsReason()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await SeedConflictsAsync(client);
        var preview = await PreviewAsync(client, 10);
        var two = preview.Conflicts.Single(c => c.Conflict.Claimant.Slug == "claimer-two").Conflict.Id;
        var one = preview.Conflicts.Single(c => c.Conflict.Claimant.Slug == "claimer-one").Conflict.Id;
        var diff = (await client.GetFromJsonAsync<PersonaConflictsReadModel>("/ops/personas/conflicts?status=Open"))!
            .Conflicts.Single(c => c.Claimant.Slug == "claimer-diff").Id;

        // Since the preview: the first was settled by hand, the second's claimant now lists another YouTube.
        (await client.PostAsJsonAsync($"/ops/personas/conflicts/{two}/resolve", new { action = "keep" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await BatchAsync(
            client, "claimer-one", "dpm",
            [Link("twitch", "https://twitch.tv/one"), Link("youtube", "https://youtube.com/@other")],
            ("Account One", "EUW"));

        var unknown = Guid.NewGuid();
        var result = await BulkAsync(client, [two, one, diff, unknown], 10);

        result.Resolved.Should().Be(0);
        result.Skipped.Should().Be(4);
        result.Outcomes.Select(o => o.Reason).Should().Equal("notOpen", "differingChannel", "differingChannel", "notFound");
        (await client.GetFromJsonAsync<PersonaConflictsReadModel>("/ops/personas/conflicts?status=Open"))!.Open.Should().Be(3);

        // Sent anyway: under its floor, or sharing reference pages only, a conflict is not merged in bulk.
        var refs = (await client.GetFromJsonAsync<PersonaConflictsReadModel>("/ops/personas/conflicts?status=Open"))!
            .Conflicts.Single(c => c.Claimant.Slug == "claimer-refs").Id;
        (await BulkAsync(client, [refs], 10)).Outcomes.Single().Reason.Should().Be("belowFloor");
    }

    [Fact]
    public async Task Resolve_RejectsAnEmptyOrOversizedBatch_ALowFloor_AndNeedsTheOpsKey()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        (await client.PostAsJsonAsync("/ops/personas/conflicts/bulk-resolve", new { ids = Array.Empty<Guid>(), minScore = 20 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var tooMany = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();
        (await client.PostAsJsonAsync("/ops/personas/conflicts/bulk-resolve", new { ids = tooMany, minScore = 20 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/ops/personas/conflicts/bulk-resolve", new { ids = new[] { Guid.NewGuid() }, minScore = 3 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        (await anonymous.PostAsJsonAsync("/ops/personas/conflicts/bulk-resolve", new { ids = new[] { Guid.NewGuid() }, minScore = 20 }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/ops/personas/conflicts/bulk-resolve/preview?minScore=20"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Four conflicts, holders first (so each holder is the older): two shared channels (20), one
    /// (10), one shared plus one differing (5), four shared reference pages and no channel (12).
    /// </summary>
    private static async Task SeedConflictsAsync(HttpClient client)
    {
        await BatchAsync(client, "holder-two", "leaguepedia",
            [Link("twitch", "https://www.twitch.tv/two"), Link("x", "https://x.com/two")], ("Account Two", "EUW"));
        await BatchAsync(client, "claimer-two", "dpm",
            [Link("twitch", "https://twitch.tv/two/"), Link("x", "https://x.com/two")], ("Account Two", "EUW"));
        await BatchAsync(client, "holder-one", "leaguepedia",
            [Link("twitch", "https://twitch.tv/one"), Link("youtube", "https://youtube.com/@one")], ("Account One", "EUW"));
        await BatchAsync(client, "claimer-one", "dpm", [Link("twitch", "https://twitch.tv/one")], ("Account One", "EUW"));
        await BatchAsync(client, "holder-diff", "leaguepedia",
            [Link("twitch", "https://twitch.tv/diff"), Link("youtube", "https://youtube.com/@a")], ("Account Diff", "EUW"));
        await BatchAsync(client, "claimer-diff", "dpm",
            [Link("twitch", "https://twitch.tv/diff"), Link("youtube", "https://youtube.com/@b")], ("Account Diff", "EUW"));
        object[] references =
        [
            Link("leaguepedia", "https://lol.fandom.com/wiki/Refs"), Link("lolpros", "https://lolpros.gg/player/refs"),
            Link("deeplol", "https://deeplol.gg/pro/refs"), Link("website", "https://refs.example"),
        ];
        await BatchAsync(client, "holder-refs", "leaguepedia", references, ("Account Refs", "EUW"));
        await BatchAsync(client, "claimer-refs", "dpm", references, ("Account Refs", "EUW"));
    }

    private static object Link(string kind, string url) => new { kind, url };

    private static async Task BatchAsync(
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
    }

    private static async Task<PersonaConflictBulkPreviewReadModel> PreviewAsync(HttpClient client, int minScore) =>
        (await client.GetFromJsonAsync<PersonaConflictBulkPreviewReadModel>($"/ops/personas/conflicts/bulk-resolve/preview?minScore={minScore}"))!;

    private static async Task<PersonaConflictBulkResolveResponse> BulkAsync(HttpClient client, IReadOnlyList<Guid> ids, int minScore)
    {
        var response = await client.PostAsJsonAsync("/ops/personas/conflicts/bulk-resolve", new { ids, minScore });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PersonaConflictBulkResolveResponse>())!;
    }

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
