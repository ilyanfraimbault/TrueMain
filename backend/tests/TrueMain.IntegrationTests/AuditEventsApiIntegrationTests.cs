using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Logging.Mongo;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Bson;
using TrueMain.ReadModels.Ops;

namespace TrueMain.IntegrationTests;

/// <summary>
/// <c>GET /ops/audit-events</c> (#2288): the operator-action trail the writers record, newest first,
/// worded, and filtered the way the admin Audit page and the known-player History ask for it.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class AuditEventsApiIntegrationTests : IAsyncLifetime
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _postgres;
    private readonly MongoFixture _mongo;

    public AuditEventsApiIntegrationTests(PostgresFixture postgres, MongoFixture mongo)
    {
        _postgres = postgres;
        _mongo = mongo;
    }

    public async ValueTask InitializeAsync()
    {
        await _postgres.ResetDatabaseAsync();
        await _mongo.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task ARename_ShowsUpWorded_InTheTrailAndInThePlayersHistory()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        var created = await client.PostAsJsonAsync("/ops/personas", new { slug = "caps", displayName = "Caps" });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<PersonaEditedResponse>())!.Id;
        (await client.PatchAsJsonAsync($"/ops/personas/{id}", new { displayName = "Caps (G2)" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await InsertAsync(AuditActions.SeedAccount, AuditActors.Operator, AuditTargets.SeedRequest, Guid.NewGuid().ToString(), 5,
            ("gameName", "Faker"), ("tagLine", "KR1"));

        var all = await GetAsync(client, "/ops/audit-events");
        all.Total.Should().Be(3);
        all.Entries.Select(e => e.Action).Should().Equal(
            AuditActions.RenamePersona, AuditActions.CreatePersona, AuditActions.SeedAccount);
        var rename = all.Entries[0];
        rename.Label.Should().Be("Renamed a known player");
        rename.Actor.Should().Be("operator");
        rename.TargetType.Should().Be("Persona");
        rename.TargetId.Should().Be(id.ToString());
        rename.Metadata.Should().Contain(new KeyValuePair<string, string>("from", "Caps"))
            .And.Contain(new KeyValuePair<string, string>("to", "Caps (G2)"))
            .And.Contain(new KeyValuePair<string, string>("slug", "caps"));
        all.Actions.Should().Contain(new AuditActionReadModel(AuditActions.MergePersonas, "Merged known players"));
        all.Actors.Should().Equal("operator", "auto", "ingestor");
        all.TargetTypes.Should().Contain("SeedRequest");

        // The History: the same endpoint on one target, its id in any casing.
        var history = await GetAsync(client, $"/ops/audit-events?targetType=persona&targetId={id.ToString().ToUpperInvariant()}");
        history.Entries.Select(e => e.Action).Should().Equal(AuditActions.RenamePersona, AuditActions.CreatePersona);
    }

    [Fact]
    public async Task Filters_NarrowByAction_Actor_Window_AndMetadataText_AndPage()
    {
        var target = Guid.NewGuid().ToString();
        await InsertAsync(AuditActions.MergePersonas, AuditActors.Auto, AuditTargets.Persona, target, 60, ("merged", "caps-dpm"));
        await InsertAsync(AuditActions.RemovePersonaLink, AuditActors.Operator, AuditTargets.Persona, target, 30, ("url", "https://x.com/Caps"));
        await InsertAsync(AuditActions.ResolvePersonaConflict, AuditActors.Operator, AuditTargets.PersonaAccountConflict, Guid.NewGuid().ToString(), 10);
        await InsertAsync(AuditActions.SeedAccountIngested, AuditActors.Ingestor, AuditTargets.SeedRequest, Guid.NewGuid().ToString(), 1);
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        (await GetAsync(client, "/ops/audit-events?action=MERGE_PERSONAS")).Entries.Should().ContainSingle()
            .Which.Label.Should().Be("Merged known players");
        (await GetAsync(client, "/ops/audit-events?actor=Operator")).Total.Should().Be(2);
        (await GetAsync(client, "/ops/audit-events?search=x.COM/caps")).Entries.Should().ContainSingle()
            .Which.Action.Should().Be(AuditActions.RemovePersonaLink);
        (await GetAsync(client, "/ops/audit-events?search=.*")).Total.Should().Be(0);

        var since = Uri.EscapeDataString(DateTime.UtcNow.AddMinutes(-45).ToString("o"));
        var until = Uri.EscapeDataString(DateTime.UtcNow.AddMinutes(-5).ToString("o"));
        (await GetAsync(client, $"/ops/audit-events?since={since}&until={until}")).Entries.Select(e => e.Action)
            .Should().Equal(AuditActions.ResolvePersonaConflict, AuditActions.RemovePersonaLink);

        var second = await GetAsync(client, "/ops/audit-events?page=2&pageSize=3");
        second.Total.Should().Be(4);
        second.Page.Should().Be(2);
        second.Entries.Should().ContainSingle().Which.Action.Should().Be(AuditActions.MergePersonas);
    }

    [Fact]
    public async Task AnUnknownAction_IsShownByItsName_AndTheEndpointNeedsTheOpsKey()
    {
        await InsertAsync("retired_action", "system", null, null, 1);
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var entry = (await GetAsync(client, "/ops/audit-events")).Entries.Single();
        entry.Label.Should().Be("retired_action");
        entry.Metadata.Should().BeEmpty();

        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        (await anonymous.GetAsync("/ops/audit-events")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task InsertAsync(
        string action, string actor, string? targetType, string? targetId, int minutesAgo, params (string Key, string Value)[] metadata) =>
        await _mongo.GetCollection<AuditEventDocument>(MongoFixture.AuditCollection).InsertOneAsync(new AuditEventDocument
        {
            Id = ObjectId.GenerateNewId(),
            Action = action,
            Actor = actor,
            TargetType = targetType,
            TargetId = targetId,
            Metadata = metadata.Length == 0 ? null : metadata.ToDictionary(m => m.Key, m => m.Value),
            TimestampUtc = DateTime.UtcNow.AddMinutes(-minutesAgo),
        });

    private static async Task<AuditEventsReadModel> GetAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AuditEventsReadModel>())!;
    }

    private ApiWebApplicationFactory CreateFactory() => new(_postgres, _mongo);

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }

    private sealed class ApiWebApplicationFactory(PostgresFixture postgres, MongoFixture mongo)
        : TrueMainWebApplicationFactory<Program>(
            postgres,
            [
                new KeyValuePair<string, string?>("MongoLogging:ConnectionString", mongo.ConnectionString),
                new KeyValuePair<string, string?>("MongoLogging:Database", MongoFixture.DatabaseName),
                new KeyValuePair<string, string?>("MongoLogging:AuditCollection", MongoFixture.AuditCollection),
                new KeyValuePair<string, string?>("MongoLogging:MinimumLevel", "None"),
            ]);
}
