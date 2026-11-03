using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The admin sidebar's summary (#2294): the open conflicts and pending homonym groups, counted and
/// cached a few seconds, and the health verdict as the warm cockpit payload holds it — unknown
/// until a cockpit read has evaluated one, never evaluated here.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class NavSummaryApiIntegrationTests : IAsyncLifetime
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;
    private readonly MongoFixture _mongo;

    public NavSummaryApiIntegrationTests(PostgresFixture fixture, MongoFixture mongo)
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
    public async Task NavSummary_CountsTheQueues_AndReadsHealthOnlyOnceTheCockpitEvaluatedIt()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "holder", "Holder", "leaguepedia", "Someone");
        await BatchAsync(client, "claimer", "Claimer", "dpm", "Someone");
        await BatchAsync(client, "rain", "Rain", "leaguepedia", "Rain One");
        await BatchAsync(client, "rain-dpm", "R.A.I.N", "dpm", "Rain Two");
        await BatchAsync(client, "other", "Other", "leaguepedia", "Other");

        var cold = await GetAsync<NavSummaryReadModel>(client, "/ops/nav-summary");

        cold.OpenConflicts.Should().Be(1);
        cold.HomonymGroups.Should().Be(1);
        cold.HealthStatus.Should().Be("unknown");
        cold.HealthEvaluatedAtUtc.Should().BeNull();

        var health = await GetAsync<PipelineHealthReadModel>(client, "/ops/pipeline-health");
        var warm = await GetAsync<NavSummaryReadModel>(client, "/ops/nav-summary");

        warm.HealthStatus.Should().Be(health.Status);
        warm.HealthEvaluatedAtUtc.Should().Be(health.EvaluatedAtUtc);
    }

    [Fact]
    public async Task NavSummary_ServesItsCountsFromCache_AndCountsMarkedHomonymsSettled()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "kim-a", "Kim", "leaguepedia", "Kim A");
        await BatchAsync(client, "kim-b", "KIM", "dpm", "Kim B");
        (await GetAsync<NavSummaryReadModel>(client, "/ops/nav-summary")).HomonymGroups.Should().Be(1);

        await using (var db = _fixture.CreateDbContext())
        {
            var ids = await db.Personas.Where(p => p.Slug.StartsWith("kim-")).Select(p => p.Id).ToListAsync();
            (await client.PostAsJsonAsync("/ops/personas/homonyms/distinct", new { personaIds = ids })).EnsureSuccessStatusCode();
        }

        (await GetAsync<NavSummaryReadModel>(client, "/ops/nav-summary")).HomonymGroups.Should().Be(1, "the counts are cached");

        await using var fresh = CreateFactory();
        using var freshClient = CreateClient(fresh);
        (await GetAsync<NavSummaryReadModel>(freshClient, "/ops/nav-summary")).HomonymGroups.Should().Be(0);
    }

    [Fact]
    public async Task NavSummary_NeedsTheOpsKey()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        (await anonymous.GetAsync("/ops/nav-summary")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NameKey_IsComputedByPostgres_AndFollowsARename()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await BatchAsync(client, "zdr", "Z.D.R Main", "leaguepedia", "Zdr");

        await using var db = _fixture.CreateDbContext();
        var persona = await db.Personas.SingleAsync(p => p.Slug == "zdr");
        persona.NameKey.Should().Be("zdrmain");

        persona.DisplayName = "Other_Name";
        await db.SaveChangesAsync();
        persona.NameKey.Should().Be("othername");
    }

    private static async Task BatchAsync(HttpClient client, string slug, string displayName, string source, string gameName)
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
                    links = Array.Empty<object>(),
                    accounts = new[] { new { gameName, tagLine = "EUW", platformId = "EUW1" } },
                },
            },
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string url) => (await client.GetFromJsonAsync<T>(url))!;

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
