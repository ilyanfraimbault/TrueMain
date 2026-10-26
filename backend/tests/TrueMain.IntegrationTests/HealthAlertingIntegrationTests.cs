using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Data.Entities;
using Data.Logging.Mongo;
using Data.Ops.Mongo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Alerting;
using TrueMain.Services.Ops.Health;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The pipeline-health alerter (#2292) against a real Mongo and the real cockpit evaluation: a
/// failing process turns the verdict red, exactly one message reaches the (fake) Discord
/// endpoint, one recovery follows, both are recorded, and the configuration page never shows
/// the webhook.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class HealthAlertingIntegrationTests(PostgresFixture fixture, MongoFixture mongo) : IAsyncLifetime
{
    private const string Webhook = "https://discord.test/api/webhooks/42/never-printed-token";
    private static readonly string OpsApiKey = TrueMainWebApplicationFactory<Program>.DefaultOpsApiKey;

    private static readonly string[] WatchedProcesses =
    [
        "Discovery",
        "Scoring",
        "MatchIngestion",
        "MainAnalysis",
        "MatchParticipantEloBracketEnrichment",
        "ChampionPatternAggregation",
        "ChampionMatchupLeadAggregation",
        "AccountRefresh",
        "MatchDataRetention"
    ];

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetDatabaseAsync();
        await mongo.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task AFailingProcess_SendsOneAlert_ThenOneRecovery_AndTheHealthPanelListsBoth()
    {
        using var discord = new RecordingHandler();
        await using var factory = new AlertingFactory(fixture, mongo, discord, Webhook);
        using var client = CreateClient(factory);
        var alerter = factory.Services.GetServices<IHostedService>().OfType<PipelineHealthAlerter>().Single();
        // Every watched process has a run, so the processes signal is measured (never-ran is unknown).
        foreach (var process in WatchedProcesses)
        {
            await SeedRunAsync(process, ProcessRunStatus.Success, secondsAgo: 300);
        }

        await SeedRunAsync("MatchIngestion", ProcessRunStatus.Failed, secondsAgo: 60);

        (await alerter.EvaluateOnceAsync(CancellationToken.None)).Should().BeNull("the first pass after boot only observes");
        var opened = await EvaluateFreshAsync(factory, alerter);
        (await EvaluateFreshAsync(factory, alerter)).Should().BeNull("a held red is not a transition");

        await SeedRunAsync("MatchIngestion", ProcessRunStatus.Success, secondsAgo: 20);
        var recovered = await EvaluateFreshAsync(factory, alerter);

        opened!.Delivered.Should().BeTrue();
        opened.Changes.Select(change => change.Key).Should().Contain("processes");
        recovered!.Kind.Should().Be("recovered");
        discord.Requests.Should().HaveCount(2);
        discord.Requests.Should().OnlyContain(request => request.Uri == Webhook && request.Method == "POST");
        discord.Requests[0].Body.Should().Contain("\"color\":15026253").And.Contain("last ended badly: MatchIngestion");
        discord.Requests[1].Body.Should().Contain("recovered");

        var panel = await client.GetFromJsonAsync<HealthAlertsReadModel>("/ops/health-alerts");
        panel!.Enabled.Should().BeTrue();
        panel.Channel.Should().Be("discord");
        panel.OpenIncidents.Should().NotContain("processes");
        panel.LastEvaluatedAtUtc.Should().NotBeNull();
        panel.Alerts.Select(alert => alert.Kind).Should().Equal("recovered", "failing");
    }

    [Fact]
    public async Task TheConfigurationPage_SaysAlertingIsOn_WithoutTheWebhook()
    {
        using var discord = new RecordingHandler();
        await using var factory = new AlertingFactory(fixture, mongo, discord, Webhook);
        using var client = CreateClient(factory);

        var body = await client.GetStringAsync("/ops/configuration");

        body.Should().Contain("Alerting:Enabled").And.NotContain("never-printed-token").And.NotContain("discord.test");
    }

    [Fact]
    public async Task WithoutAWebhook_ThePanelSaysAlertingIsOff()
    {
        using var discord = new RecordingHandler();
        await using var factory = new AlertingFactory(fixture, mongo, discord, webhook: null);
        using var client = CreateClient(factory);

        var panel = await client.GetFromJsonAsync<HealthAlertsReadModel>("/ops/health-alerts");

        panel!.Enabled.Should().BeFalse();
        panel.Channel.Should().BeNull();
        panel.Alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task TheStateSave_RefusesAStaleVersion()
    {
        var settings = new MongoLoggingOptions { ConnectionString = mongo.ConnectionString, Database = MongoFixture.DatabaseName };
        using var context = new MongoLogContext(Microsoft.Extensions.Options.Options.Create(settings));
        var store = new HealthAlertStore(context, Microsoft.Extensions.Options.Options.Create(settings));

        (await store.TrySaveStateAsync(new HealthAlertStateDocument(), 0, CancellationToken.None)).Should().BeTrue();
        (await store.TrySaveStateAsync(new HealthAlertStateDocument(), 0, CancellationToken.None)).Should().BeFalse("version 1 is stored");
        (await store.TrySaveStateAsync(new HealthAlertStateDocument { LastEvaluatedAtUtc = DateTime.UtcNow }, 1, CancellationToken.None))
            .Should().BeTrue();

        var saved = await store.GetStateAsync(CancellationToken.None);
        saved!.Version.Should().Be(2);
        saved.LastEvaluatedAtUtc.Should().NotBeNull();
    }

    /// <summary>One alerter pass on a recomputed verdict — the cached one is fresh for 30 s, longer than this test.</summary>
    private static Task<HealthAlertDocument?> EvaluateFreshAsync(AlertingFactory factory, PipelineHealthAlerter alerter)
    {
        factory.Services.GetRequiredService<IMemoryCache>().Remove(CachedPipelineHealthQueryService.Payload.Key);
        return alerter.EvaluateOnceAsync(CancellationToken.None);
    }

    private Task SeedRunAsync(string processName, ProcessRunStatus status, int secondsAgo)
    {
        var startedAt = DateTime.UtcNow.AddSeconds(-secondsAgo);
        return mongo.GetCollection<ProcessRunDocument>(MongoFixture.ProcessRunsCollection).InsertOneAsync(new ProcessRunDocument
        {
            Id = Guid.NewGuid(),
            ProcessName = processName,
            StartedAtUtc = startedAt,
            FinishedAtUtc = startedAt.AddSeconds(5),
            DurationMs = 5_000,
            Status = status,
            Error = status == ProcessRunStatus.Failed ? "boom" : null,
            Host = "test-host"
        });
    }

    private static HttpClient CreateClient(AlertingFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Ops-Key", OpsApiKey);
        return client;
    }

    private sealed class AlertingFactory(PostgresFixture fixture, MongoFixture mongo, RecordingHandler discord, string? webhook)
        : TrueMainWebApplicationFactory<Program>(
            fixture,
            [
                new KeyValuePair<string, string?>("MongoLogging:ConnectionString", mongo.ConnectionString),
                new KeyValuePair<string, string?>("MongoLogging:Database", MongoFixture.DatabaseName),
                new KeyValuePair<string, string?>("MongoLogging:MinimumLevel", "None"),
                new KeyValuePair<string, string?>("Alerting:DiscordWebhookUrl", webhook),
                new KeyValuePair<string, string?>("Alerting:AdminBaseUrl", "https://admin.example.test"),
                // The loop's own timer never fires during a test: each pass is driven by hand.
                new KeyValuePair<string, string?>("Alerting:EvaluationInterval", "1.00:00:00")
            ])
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services
                .AddHttpClient(DiscordHealthAlertNotifier.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => discord));
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private readonly List<RecordedRequest> _requests = [];

        public IReadOnlyList<RecordedRequest> Requests
        {
            get
            {
                lock (_gate)
                {
                    return [.. _requests];
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_gate)
            {
                _requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!.ToString(), body));
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private sealed record RecordedRequest(string Method, string Uri, string Body);
}
