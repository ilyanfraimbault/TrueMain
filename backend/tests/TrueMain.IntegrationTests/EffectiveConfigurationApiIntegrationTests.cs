using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Data.Configuration;
using Data.Ops.Mongo;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Driver;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Contract cover for <c>GET /ops/configuration</c> (#1034). The catalogue guard lives in
/// <c>EffectiveConfigurationCatalogTests</c> — what needs a real host and a real Mongo is
/// the other half: that the Api's snapshot is built live from the container it is answering
/// from, that the Ingestor's is merged in from what it published at boot, and that neither
/// path lets a secret out.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class EffectiveConfigurationApiIntegrationTests
{
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;

    private readonly PostgresFixture _postgres;
    private readonly MongoFixture _mongo;

    public EffectiveConfigurationApiIntegrationTests(PostgresFixture postgres, MongoFixture mongo)
    {
        _postgres = postgres;
        _mongo = mongo;
    }

    [Fact]
    public async Task Requires_the_ops_api_key()
    {
        await ResetAsync();

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/ops/configuration");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Returns_the_api_snapshot_with_a_stable_shape_even_when_nothing_was_published()
    {
        await ResetAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/ops/configuration");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["processes"]);

        var process = document.RootElement.GetProperty("processes")[0];
        process.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["processName", "environment", "version", "capturedAtUtc", "sections"]);
        process.GetProperty("sections")[0].EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["name", "title", "description", "values"]);
        process.GetProperty("sections")[0].GetProperty("values")[0].EnumerateObject()
            .Select(property => property.Name)
            .Should().BeEquivalentTo(
                ["key", "name", "value", "valueLabel", "origin", "source", "unit", "notice"]);

        var payload = await response.Content.ReadFromJsonAsync<ConfigurationTestContract>();
        payload.Should().NotBeNull();

        // Nothing has published, so the Api's live snapshot is the whole page. A missing
        // Ingestor is a gap the page reports, never a reason to fail the request.
        var api = payload!.Processes.Should().ContainSingle().Subject;
        api.ProcessName.Should().Be("Api");
        api.Environment.Should().Be("Testing", "the snapshot names the environment the host really booted in");
        api.Sections.Select(section => section.Name).Should().Equal(
            "MainAnalysis", "Database", "MongoLogging", "ChampionsList", "DataQualityDetectors", "StorageHistory");
        api.Sections.Should().OnlyContain(section => section.Values.Count > 0);
        api.Sections.Should().OnlyContain(section => section.Description.Length > 0);
    }

    [Fact]
    public async Task Never_exposes_a_secret_bearing_key_from_a_section_that_holds_one()
    {
        await ResetAsync();

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var json = await client.GetStringAsync("/ops/configuration");

        // MongoLogging is the section that both carries a credential and is worth showing,
        // so it is the one that proves the allow-list holds over the wire. The connection
        // string is a live, working value in this host — if the include-list ever widened,
        // the real secret would be in this response.
        json.Should().NotContain("ConnectionString");
        json.Should().NotContain(_mongo.ConnectionString);
        json.Should().NotContain("ApiKey");
        json.Should().NotContain(OpsApiKey);

        var payload = await client.GetFromJsonAsync<ConfigurationTestContract>("/ops/configuration");
        var mongoSection = payload!.Processes
            .Single(process => process.ProcessName == "Api")
            .Sections.Single(section => section.Name == "MongoLogging");

        // Positively: the retention windows an operator comes here for are present, so the
        // section is narrowed rather than emptied.
        mongoSection.Values.Select(value => value.Name).Should().Contain("LogsRetention");
        mongoSection.Values.Select(value => value.Name).Should().NotContain("Database");
        mongoSection.Values.Select(value => value.Name).Should().NotContain("CrashFilePath");
    }

    [Fact]
    public async Task Marks_a_value_the_container_really_overrides_and_clears_its_notice()
    {
        await ResetAsync();

        await using var unset = CreateFactory();
        using var unsetClient = CreateClient(unset);
        var unsetPayload = await unsetClient.GetFromJsonAsync<ConfigurationTestContract>("/ops/configuration");
        var unsetCapacity = ValueOf(unsetPayload!, "StorageHistory", "DiskCapacityBytes");

        // Unset: the page must say what that costs elsewhere rather than print a bare 0.
        unsetCapacity.Origin.Should().Be(EffectiveConfigurationOrigins.Default);
        unsetCapacity.Source.Should().BeNull();
        unsetCapacity.Notice.Should().NotBeNullOrWhiteSpace();

        await using var overridden = new ApiWebApplicationFactory(
            _postgres,
            _mongo,
            [new KeyValuePair<string, string?>("StorageHistory:DiskCapacityBytes", "1099511627776")]);
        using var overriddenClient = CreateClient(overridden);
        var overriddenPayload = await overriddenClient.GetFromJsonAsync<ConfigurationTestContract>(
            "/ops/configuration");
        var setCapacity = ValueOf(overriddenPayload!, "StorageHistory", "DiskCapacityBytes");

        // Set: origin names that a provider supplied it, source names which one, and the
        // notice disappears because the consequence it warned about no longer applies.
        setCapacity.Value.Should().Be("1099511627776");
        setCapacity.Origin.Should().Be(EffectiveConfigurationOrigins.Override);
        setCapacity.Source.Should().Be("in-memory");
        setCapacity.Notice.Should().BeNull();
        // The humanised form is what the page prints beside the raw number.
        setCapacity.ValueLabel.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Merges_the_published_ingestor_snapshot_beside_the_live_api_one()
    {
        await ResetAsync();
        var publishedAt = DateTime.UtcNow.AddHours(-6);
        await PublishAsync("Ingestor", publishedAt, "9.9.9");

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var payload = await client.GetFromJsonAsync<ConfigurationTestContract>("/ops/configuration");

        // Sorted by process name, so the page's column order does not depend on Mongo's.
        payload!.Processes.Select(process => process.ProcessName).Should().Equal("Api", "Ingestor");

        var ingestor = payload.Processes.Single(process => process.ProcessName == "Ingestor");
        ingestor.Version.Should().Be("9.9.9");
        // The Ingestor's boot time, not "now": an operator has to be able to see that the
        // snapshot predates the last deploy.
        ingestor.CapturedAtUtc.Should().BeCloseTo(publishedAt, TimeSpan.FromSeconds(1));
        ingestor.Sections.Should().ContainSingle().Which.Values.Should().ContainSingle()
            .Which.Key.Should().Be("Harvest:BatchSize");

        var api = payload.Processes.Single(process => process.ProcessName == "Api");
        api.CapturedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5),
            "the Api's snapshot is built live on every request");
    }

    [Fact]
    public async Task Lets_the_live_api_snapshot_shadow_a_stale_one_some_past_build_published()
    {
        await ResetAsync();
        // A document written by an older build of this same process. Serving it would tell
        // an operator the Api runs on settings it demonstrably does not.
        await PublishAsync("Api", DateTime.UtcNow.AddDays(-30), "0.0.1-stale");

        await using var factory = CreateFactory();
        using var client = CreateClient(factory);

        var payload = await client.GetFromJsonAsync<ConfigurationTestContract>("/ops/configuration");

        var api = payload!.Processes.Should().ContainSingle().Subject;
        api.ProcessName.Should().Be("Api");
        api.Version.Should().NotBe("0.0.1-stale");
        api.CapturedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
        api.Sections.Select(section => section.Name).Should().NotContain("Harvest");
    }

    private static EffectiveConfigurationValueTestContract ValueOf(
        ConfigurationTestContract payload,
        string sectionName,
        string valueName)
        => payload.Processes
            .Single(process => process.ProcessName == "Api")
            .Sections.Single(section => section.Name == sectionName)
            .Values.Single(value => value.Name == valueName);

    private async Task PublishAsync(string processName, DateTime capturedAtUtc, string version)
    {
        var store = _mongo.GetCollection<EffectiveConfigurationDocument>(
            MongoFixture.EffectiveConfigurationCollection);

        await store.InsertOneAsync(new EffectiveConfigurationDocument
        {
            ProcessName = processName,
            Environment = "Testing",
            Version = version,
            CapturedAtUtc = capturedAtUtc,
            Sections =
            [
                new EffectiveConfigurationSectionDocument
                {
                    Name = "Harvest",
                    Title = "Harvest",
                    Description = "How many accounts the harvest claims per cycle.",
                    Values =
                    [
                        new EffectiveConfigurationValueDocument
                        {
                            Key = "Harvest:BatchSize",
                            Name = "BatchSize",
                            Value = "250",
                            Origin = EffectiveConfigurationOrigins.Override,
                            Source = "environment",
                            Unit = "count"
                        }
                    ]
                }
            ]
        });
    }

    private async Task ResetAsync()
    {
        await _postgres.ResetDatabaseAsync();
        await _mongo.ResetAsync();
    }

    private ApiWebApplicationFactory CreateFactory() => new(_postgres, _mongo);

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }

    private sealed class ApiWebApplicationFactory : TrueMainWebApplicationFactory<Program>
    {
        public ApiWebApplicationFactory(
            PostgresFixture postgres,
            MongoFixture mongo,
            IReadOnlyCollection<KeyValuePair<string, string?>>? extra = null)
            : base(postgres, Compose(mongo, extra))
        {
        }

        private static List<KeyValuePair<string, string?>> Compose(
            MongoFixture mongo,
            IReadOnlyCollection<KeyValuePair<string, string?>>? extra)
        {
            var settings = new List<KeyValuePair<string, string?>>
            {
                new("MongoLogging:ConnectionString", mongo.ConnectionString),
                new("MongoLogging:Database", MongoFixture.DatabaseName),
                new("MongoLogging:LogsCollection", MongoFixture.LogsCollection),
                new("MongoLogging:AuditCollection", MongoFixture.AuditCollection),
                new("MongoLogging:EffectiveConfigurationCollection",
                    MongoFixture.EffectiveConfigurationCollection),
                new("MongoLogging:MinimumLevel", "None")
            };

            if (extra is { Count: > 0 })
            {
                settings.AddRange(extra);
            }

            return settings;
        }
    }

    private sealed class ConfigurationTestContract
    {
        public IReadOnlyList<EffectiveConfigurationProcessTestContract> Processes { get; init; } = [];
    }

    private sealed class EffectiveConfigurationProcessTestContract
    {
        public string ProcessName { get; init; } = string.Empty;

        public string Environment { get; init; } = string.Empty;

        public string? Version { get; init; }

        public DateTime CapturedAtUtc { get; init; }

        public IReadOnlyList<EffectiveConfigurationSectionTestContract> Sections { get; init; } = [];
    }

    private sealed class EffectiveConfigurationSectionTestContract
    {
        public string Name { get; init; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public IReadOnlyList<EffectiveConfigurationValueTestContract> Values { get; init; } = [];
    }

    private sealed class EffectiveConfigurationValueTestContract
    {
        public string Key { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string? Value { get; init; }

        public string? ValueLabel { get; init; }

        public string Origin { get; init; } = string.Empty;

        public string? Source { get; init; }

        public string Unit { get; init; } = string.Empty;

        public string? Notice { get; init; }
    }
}
