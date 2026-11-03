using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Ops.Mongo;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Driver;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Contract cover for the deploy markers' backend (#2297): the Api appends its own boot at
/// startup, classified against the previous one, and <c>GET /ops/deployments</c> lists only the
/// boots that changed the version.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class DeploymentsApiIntegrationTests : IAsyncLifetime
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;

    private readonly PostgresFixture _postgres;
    private readonly MongoFixture _mongo;

    public DeploymentsApiIntegrationTests(PostgresFixture postgres, MongoFixture mongo)
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

    private IMongoCollection<ProcessBootDocument> Boots =>
        _mongo.GetCollection<ProcessBootDocument>(MongoFixture.ProcessBootsCollection);

    [Fact]
    public async Task RequiresTheOpsApiKey()
    {
        await using var factory = CreateFactory(version: null);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/deployments");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RecordsTheApiBootAsADeployWhenItsVersionChanged()
    {
        var previousDeploy = DateTime.UtcNow.AddDays(-3);
        await Boots.InsertOneAsync(new ProcessBootDocument
        {
            ProcessName = "Api",
            Environment = "Testing",
            Version = "1.9.0",
            VersionChanged = true,
            StartedAtUtc = previousDeploy
        });

        await using var factory = CreateFactory(version: "2.0.0");
        using var client = CreateClient(factory);

        var payload = await client.GetFromJsonAsync<DeploymentsTestContract>("/ops/deployments");

        payload!.Deployments.Select(deploy => deploy.Version).Should().Equal("1.9.0", "2.0.0");
        var current = payload.Deployments[^1];
        current.ProcessName.Should().Be("Api");
        current.Role.Should().BeNull();
        current.PreviousVersion.Should().Be("1.9.0");
        current.DeployedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
        payload.Deployments[0].DeployedAtUtc.Should().BeCloseTo(previousDeploy, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task DoesNotCountARestartOnTheSameVersionAsADeploy()
    {
        await BootAsync("2.0.0");
        await BootAsync("2.0.0");
        await BootAsync(null);

        (await Boots.CountDocumentsAsync(FilterDefinition<ProcessBootDocument>.Empty))
            .Should().Be(3, "every boot is appended, restarts included");

        await using var factory = CreateFactory(version: "2.0.0");
        using var client = CreateClient(factory);
        var payload = await client.GetFromJsonAsync<DeploymentsTestContract>("/ops/deployments");

        payload!.Deployments.Should().ContainSingle().Which.Version.Should().Be("2.0.0");
    }

    [Fact]
    public async Task ListsOnlyTheChangesInsideTheRequestedWindow()
    {
        var now = DateTime.UtcNow;
        await Boots.InsertManyAsync(
        [
            Change("Ingestor", "FetchWorkers", "1.0.0", now.AddDays(-20)),
            Change("Ingestor", "FetchWorkers", "1.1.0", now.AddDays(-10)),
            Change("Ingestor", "FetchWorkers", "1.2.0", now.AddDays(-2))
        ]);

        await using var factory = CreateFactory(version: null);
        using var client = CreateClient(factory);
        var from = now.AddDays(-15).ToString("O");
        var to = now.AddDays(-5).ToString("O");

        var payload = await client.GetFromJsonAsync<DeploymentsTestContract>(
            $"/ops/deployments?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");

        var deploy = payload!.Deployments.Should().ContainSingle().Subject;
        deploy.Version.Should().Be("1.1.0");
        deploy.Role.Should().Be("FetchWorkers");
    }

    [Fact]
    public async Task RejectsAWindowThatEndsBeforeItStarts()
    {
        await using var factory = CreateFactory(version: null);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/ops/deployments?from=2026-10-20T00:00:00Z&to=2026-10-01T00:00:00Z");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Boots one Api host and waits for its boot record, the way a restart would.</summary>
    private async Task BootAsync(string? version)
    {
        var before = await Boots.CountDocumentsAsync(FilterDefinition<ProcessBootDocument>.Empty);
        await using var factory = CreateFactory(version);
        using var client = CreateClient(factory);
        (await client.GetAsync("/ops/deployments")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Boots.CountDocumentsAsync(FilterDefinition<ProcessBootDocument>.Empty)).Should().Be(before + 1);
    }

    private static ProcessBootDocument Change(string process, string? role, string version, DateTime at) => new()
    {
        ProcessName = process,
        Role = role,
        Environment = "Testing",
        Version = version,
        VersionChanged = true,
        StartedAtUtc = at
    };

    private TrueMainWebApplicationFactory<Program> CreateFactory(string? version)
    {
        var settings = new List<KeyValuePair<string, string?>>
        {
            new("MongoLogging:ConnectionString", _mongo.ConnectionString),
            new("MongoLogging:Database", MongoFixture.DatabaseName),
            new("MongoLogging:LogsCollection", MongoFixture.LogsCollection),
            new("MongoLogging:AuditCollection", MongoFixture.AuditCollection),
            new("MongoLogging:ProcessBootsCollection", MongoFixture.ProcessBootsCollection),
            new("MongoLogging:MinimumLevel", "None"),
            new("AppVersion", version)
        };

        return new TrueMainWebApplicationFactory<Program>(_postgres, settings);
    }

    private static HttpClient CreateClient(TrueMainWebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }

    private sealed class DeploymentsTestContract
    {
        public DateTime FromUtc { get; init; }

        public DateTime ToUtc { get; init; }

        public IReadOnlyList<DeploymentTestContract> Deployments { get; init; } = [];
    }

    private sealed class DeploymentTestContract
    {
        public string ProcessName { get; init; } = string.Empty;

        public string? Role { get; init; }

        public string Version { get; init; } = string.Empty;

        public string? PreviousVersion { get; init; }

        public DateTime DeployedAtUtc { get; init; }
    }
}
