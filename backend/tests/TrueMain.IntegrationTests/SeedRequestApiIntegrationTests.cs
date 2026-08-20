using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Data.Entities;
using Data.Ops.Mongo;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Driver;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class SeedRequestApiIntegrationTests
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;
    private readonly PostgresFixture _fixture;
    private readonly MongoFixture _mongo;

    public SeedRequestApiIntegrationTests(PostgresFixture fixture, MongoFixture mongo)
    {
        _fixture = fixture;
        _mongo = mongo;
    }

    [Fact]
    public async Task PostSeed_ShouldRecordPendingRequestAndReturn202()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync(
            "/ops/accounts/seed",
            new { gameName = "Phantasm", tagLine = "EUW1", platformId = "EUW1" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var accepted = await response.Content.ReadFromJsonAsync<SeedAcceptedContract>();
        accepted.Should().NotBeNull();
        accepted!.Id.Should().NotBe(Guid.Empty);
        accepted.Status.Should().Be("Pending");

        var row = await Requests().Find(FilterDefinition<SeedRequestDocument>.Empty).SingleAsync();
        row.Id.Should().Be(accepted.Id);
        row.GameName.Should().Be("Phantasm");
        row.TagLine.Should().Be("EUW1");
        row.PlatformId.Should().Be("EUW1");
        row.Status.Should().Be(SeedRequestStatus.Pending);
        row.ProcessedAtUtc.Should().BeNull();
        row.ResolvedPuuid.Should().BeNull();
    }

    [Fact]
    public async Task PostSeed_ShouldBeIdempotent_ForAnUnprocessedDuplicate()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);

        var first = await client.PostAsJsonAsync(
            "/ops/accounts/seed",
            new { gameName = "Phantasm", tagLine = "EUW1", platformId = "EUW1" });
        // Same Riot ID, different casing + surrounding whitespace + a '#' on the
        // tag: still the same logical request, so it must de-duplicate.
        var second = await client.PostAsJsonAsync(
            "/ops/accounts/seed",
            new { gameName = "  phantasm  ", tagLine = "#euw1", platformId = "EUW1" });

        var firstBody = await first.Content.ReadFromJsonAsync<SeedAcceptedContract>();
        var secondBody = await second.Content.ReadFromJsonAsync<SeedAcceptedContract>();

        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        secondBody!.Id.Should().Be(firstBody!.Id, "an unprocessed duplicate returns the existing request");

        (await Requests().CountDocumentsAsync(FilterDefinition<SeedRequestDocument>.Empty)).Should().Be(1);
    }

    [Fact]
    public async Task PostSeed_ShouldReturn400_ForUnknownPlatform()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync(
            "/ops/accounts/seed",
            new { gameName = "Phantasm", tagLine = "EUW1", platformId = "NOPE" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await Requests().CountDocumentsAsync(FilterDefinition<SeedRequestDocument>.Empty)).Should().Be(0);
    }

    [Fact]
    public async Task PostSeed_ShouldReturn400_ForMissingGameName()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync(
            "/ops/accounts/seed",
            new { gameName = "   ", tagLine = "EUW1", platformId = "EUW1" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetSeedById_ShouldReturnStableShape()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        var id = Guid.NewGuid();
        await Requests().InsertOneAsync(new SeedRequestDocument
        {
            Id = id,
            GameName = "Phantasm",
            TagLine = "EUW1",
            PlatformId = "EUW1",
            Status = SeedRequestStatus.Ingested,
            RequestedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            ProcessedAtUtc = DateTime.UtcNow,
            ResolvedPuuid = "puuid-1",
            ResolvedRiotAccountId = Guid.NewGuid()
        });

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/ops/accounts/seed/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(
            [
                "id", "gameName", "tagLine", "platformId", "status", "error",
                "requestedAtUtc", "processedAtUtc", "resolvedPuuid", "resolvedRiotAccountId"
            ]);

        document.RootElement.GetProperty("status").GetString().Should().Be("Ingested");
        document.RootElement.GetProperty("resolvedPuuid").GetString().Should().Be("puuid-1");
    }

    [Fact]
    public async Task GetSeedById_ShouldReturn404_WhenUnknown()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);

        var response = await client.GetAsync($"/ops/accounts/seed/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSeedList_ShouldReturnNewestFirstAndFilterByStatus()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        var now = DateTime.UtcNow;
        await Requests().InsertManyAsync(
        [
            Build("Older", SeedRequestStatus.Pending, now.AddMinutes(-30)),
            Build("Newer", SeedRequestStatus.Pending, now.AddMinutes(-10)),
            Build("Done", SeedRequestStatus.Ingested, now.AddMinutes(-20))
        ]);

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);

        var all = await client.GetFromJsonAsync<SeedRequestPageContract>("/ops/accounts/seed");
        all.Should().NotBeNull();
        all!.Requests.Should().HaveCount(3);
        all.Total.Should().Be(3);
        all.Requests.Should().BeInDescendingOrder(request => request.RequestedAtUtc);

        var pendingOnly = await client.GetFromJsonAsync<SeedRequestPageContract>(
            "/ops/accounts/seed?status=Pending");
        pendingOnly.Should().NotBeNull();
        pendingOnly!.Requests.Should().HaveCount(2);
        pendingOnly.Requests.Should().OnlyContain(request => request.Status == "Pending");
        // The total counts the filtered set, not the collection: a pager built on the
        // unfiltered count would offer pages the filter cannot fill.
        pendingOnly.Total.Should().Be(2);
    }

    [Fact]
    public async Task GetSeedList_ShouldPageWithoutRepeatingOrDroppingARow()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        // All five share one RequestedAtUtc — the shape a bulk seeder run produces, and
        // the one that makes paging unstable unless the sort breaks ties on id.
        var sameInstant = DateTime.UtcNow.AddMinutes(-5);
        await Requests().InsertManyAsync(
            Enumerable.Range(0, 5)
                .Select(index => Build($"Bulk{index}", SeedRequestStatus.Pending, sameInstant))
                .ToList());

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);

        var first = await client.GetFromJsonAsync<SeedRequestPageContract>(
            "/ops/accounts/seed?page=1&pageSize=2");
        var second = await client.GetFromJsonAsync<SeedRequestPageContract>(
            "/ops/accounts/seed?page=2&pageSize=2");
        var third = await client.GetFromJsonAsync<SeedRequestPageContract>(
            "/ops/accounts/seed?page=3&pageSize=2");

        first!.Total.Should().Be(5);
        first.Page.Should().Be(1);
        first.PageSize.Should().Be(2);
        first.Requests.Should().HaveCount(2);
        second!.Requests.Should().HaveCount(2);
        third!.Requests.Should().ContainSingle();

        var seen = first.Requests.Concat(second.Requests).Concat(third.Requests)
            .Select(request => request.Id)
            .ToList();
        seen.Should().OnlyHaveUniqueItems().And.HaveCount(5);
    }

    [Fact]
    public async Task GetSeedList_ShouldRejectAnUnknownRegionInsteadOfReturningNothing()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        await Requests().InsertOneAsync(
            Build("Somebody", SeedRequestStatus.Pending, DateTime.UtcNow));

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = CreateClient(factory);

        // The region filter is an exact match, so silently ignoring a typo would return an
        // empty page — which reads as "no requests in this region", a very different claim.
        var response = await client.GetAsync("/ops/accounts/seed?region=EUW");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SeedEndpoints_ShouldRequireOpsApiKey()
    {
        await _fixture.ResetDatabaseAsync();
        await _mongo.ResetAsync();

        await using var factory = new ApiWebApplicationFactory(_fixture, _mongo);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.PostAsJsonAsync(
            "/ops/accounts/seed",
            new { gameName = "Phantasm", tagLine = "EUW1", platformId = "EUW1" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private IMongoCollection<SeedRequestDocument> Requests()
        => _mongo.GetCollection<SeedRequestDocument>(MongoFixture.SeedRequestsCollection);

    private static SeedRequestDocument Build(string gameName, SeedRequestStatus status, DateTime requestedAtUtc)
        => new()
        {
            Id = Guid.NewGuid(),
            GameName = gameName,
            TagLine = "EUW1",
            PlatformId = "EUW1",
            Status = status,
            RequestedAtUtc = requestedAtUtc
        };

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }

    // Point the host at the test Mongo container (the seed-request queue lives
    // there) and mute the diagnostic sink so incidental host warnings never
    // write extra documents.
    private sealed class ApiWebApplicationFactory(PostgresFixture fixture, MongoFixture mongo)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MongoLogging:ConnectionString", mongo.ConnectionString),
                new KeyValuePair<string, string?>("MongoLogging:Database", MongoFixture.DatabaseName),
                new KeyValuePair<string, string?>("MongoLogging:MinimumLevel", "None")
            ]);

    private sealed class SeedAcceptedContract
    {
        public Guid Id { get; init; }

        public string Status { get; init; } = string.Empty;
    }

    private sealed class SeedRequestPageContract
    {
        public List<SeedRequestContract> Requests { get; init; } = [];

        public long Total { get; init; }

        public int Page { get; init; }

        public int PageSize { get; init; }
    }

    private sealed class SeedRequestContract
    {
        public Guid Id { get; init; }

        public string GameName { get; init; } = string.Empty;

        public string TagLine { get; init; } = string.Empty;

        public string PlatformId { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public DateTime RequestedAtUtc { get; init; }
    }
}
