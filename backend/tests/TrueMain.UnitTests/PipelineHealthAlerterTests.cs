using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Data.Configuration;
using Data.Ops.Mongo;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TrueMain.Configuration;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Alerting;
using static TrueMain.UnitTests.HealthAlertTransitionsTests;

namespace TrueMain.UnitTests;

/// <summary>
/// The alerter's loop around the rules (#2292): it saves before it sends, sends one message per
/// transition, records each one, retries what the channel refused, stays quiet when another
/// container claimed the pass — and the Discord body it posts.
/// </summary>
public sealed class PipelineHealthAlerterTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeSource _source = new();
    private readonly FakeStore _store = new();
    private readonly FakeNotifier _notifier = new();

    private static AlertingOptions Settings => new()
    {
        DiscordWebhookUrl = "https://discord.test/api/webhooks/1/secret",
        AdminBaseUrl = "https://admin.example.test/",
        EnvironmentLabel = "preprod",
        RedConfirmations = 2
    };

    [Fact]
    public async Task ARedHeldTwice_SendsExactlyOneAlert_ThenOneRecovery()
    {
        using var alerter = NewAlerter();

        _source.Next = Health("red");
        (await EvaluateAsync(alerter)).Should().BeNull("the first pass after boot only observes");
        var opened = await EvaluateAsync(alerter);
        (await EvaluateAsync(alerter)).Should().BeNull();
        _source.Next = Health("green");
        var recovered = await EvaluateAsync(alerter);

        _notifier.Sent.Should().HaveCount(2);
        opened!.Kind.Should().Be("failing");
        opened.Delivered.Should().BeTrue();
        opened.Title.Should().Be("[preprod] Pipeline health: 1 signal failing");
        opened.Changes.Should().ContainSingle().Which.Key.Should().Be("dataQuality");
        recovered!.Kind.Should().Be("recovered");
        recovered.Title.Should().Be("[preprod] Pipeline health recovered");
        _store.Alerts.Should().HaveCount(2);
        _notifier.Sent[0].HealthUrl.Should().Be("https://admin.example.test/health");
    }

    [Fact]
    public async Task ARestartedAlerter_ContinuesTheSavedIncident_WithoutAnnouncingItAgain()
    {
        _source.Next = Health("red");
        using var first = NewAlerter();
        await EvaluateAsync(first);
        await EvaluateAsync(first);
        await EvaluateAsync(first);

        using var restarted = NewAlerter();
        (await EvaluateAsync(restarted)).Should().BeNull();
        (await EvaluateAsync(restarted)).Should().BeNull("the incident was already announced before the restart");
        _source.Next = Health("green");
        (await EvaluateAsync(restarted))!.Kind.Should().Be("recovered", "the restart did not forget the open incident");

        _notifier.Sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task ARefusedAlert_IsRecordedUndelivered_AndRetriedNextPass()
    {
        using var alerter = NewAlerter();
        _source.Next = Health("red");
        await EvaluateAsync(alerter);
        _notifier.FailWith = HttpStatusCode.TooManyRequests;

        var refused = await EvaluateAsync(alerter);
        _notifier.FailWith = null;
        var retried = await EvaluateAsync(alerter);

        refused!.Delivered.Should().BeFalse();
        refused.Error.Should().Be("HTTP 429");
        retried!.Delivered.Should().BeTrue();
        _store.Alerts.Should().HaveCount(2);
    }

    [Fact]
    public async Task APassAnotherInstanceSavedFirst_SendsNothing()
    {
        using var alerter = NewAlerter();
        _source.Next = Health("red");
        await EvaluateAsync(alerter);
        _store.LoseNextSave = true;

        (await EvaluateAsync(alerter)).Should().BeNull();

        _notifier.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task ADisabledAlerter_ReturnsWithoutEvaluating()
    {
        using var alerter = NewAlerter(new AlertingOptions());

        await alerter.StartAsync(CancellationToken.None);
        await alerter.ExecuteTask!;

        _source.Reads.Should().Be(0);
    }

    [Fact]
    public void DiscordPayload_IsOneEmbedPerAlert_WithMentionsDisabled()
    {
        var changes = new List<HealthAlertChange>
        {
            new("process:MatchIngestion", "MatchIngestion", HealthAlertChangeKind.Failing, "Failed", "3 consecutive failed runs.", null),
            new("dataQuality", "Data quality", HealthAlertChangeKind.Recovered, "green", new string('x', 2_000), null)
        };
        var message = HealthAlertMessage.Compose(Health("red"), changes, Settings);

        var json = JsonSerializer.SerializeToElement(DiscordWebhookPayload.From(message));

        json.GetProperty("username").GetString().Should().Be("TrueMain health");
        json.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength().Should().Be(0);
        var embed = json.GetProperty("embeds").EnumerateArray().Should().ContainSingle().Subject;
        embed.GetProperty("title").GetString().Should().Be("[preprod] Pipeline health: 1 signal failing");
        embed.GetProperty("url").GetString().Should().Be("https://admin.example.test/health");
        embed.GetProperty("color").GetInt32().Should().Be(DiscordWebhookPayload.FailingColor);
        embed.GetProperty("description").GetString().Should().Contain("Verdict: **red**").And.Contain("/health");
        var fields = embed.GetProperty("fields").EnumerateArray().ToList();
        fields.Select(field => field.GetProperty("name").GetString()).Should().Equal(
            "MatchIngestion — failing (Failed)",
            "Data quality — recovered (green)");
        fields[1].GetProperty("value").GetString()!.Length.Should().Be(DiscordWebhookPayload.MaxFieldValue);
    }

    [Fact]
    public void DiscordPayload_WithoutAnAdminOrigin_NamesThePathInstead()
    {
        var changes = new List<HealthAlertChange>
        {
            new("dataQuality", "Data quality", HealthAlertChangeKind.Recovered, "green", "ok", null)
        };
        var message = HealthAlertMessage.Compose(Health("red"), changes, new AlertingOptions());

        var json = JsonSerializer.SerializeToElement(DiscordWebhookPayload.From(message));
        var embed = json.GetProperty("embeds")[0];

        message.Title.Should().Be("Pipeline health: 1 signal recovered", "the verdict is still red");
        embed.TryGetProperty("url", out _).Should().BeFalse();
        embed.GetProperty("color").GetInt32().Should().Be(DiscordWebhookPayload.RecoveredColor);
        embed.GetProperty("description").GetString().Should().Contain("Open /health in the admin portal.");
    }

    [Fact]
    public void TheConfigurationPage_ShowsWhetherAlertingIsOn_NeverTheWebhook()
    {
        var section = ApiEffectiveConfigurationCatalog.Instance.Sections
            .Single(descriptor => descriptor.SectionName == AlertingOptions.SectionName);

        EffectiveConfigurationRedaction.IsSecretName(nameof(AlertingOptions.DiscordWebhookUrl)).Should().BeTrue();
        section.IncludeProperties.Should().Contain(nameof(AlertingOptions.Enabled))
            .And.NotContain(nameof(AlertingOptions.DiscordWebhookUrl));
    }

    private PipelineHealthAlerter NewAlerter(AlertingOptions? options = null)
        => new(
            Microsoft.Extensions.Options.Options.Create(options ?? Settings),
            _source,
            _store,
            _notifier,
            _clock,
            NullLogger<PipelineHealthAlerter>.Instance);

    private async Task<HealthAlertDocument?> EvaluateAsync(PipelineHealthAlerter alerter)
    {
        _clock.Advance(TimeSpan.FromMinutes(5));
        return await alerter.EvaluateOnceAsync(CancellationToken.None);
    }

    private sealed class FakeSource : IPipelineHealthSnapshotSource
    {
        public PipelineHealthReadModel Next { get; set; } = Health("green");

        public int Reads { get; private set; }

        public Task<PipelineHealthReadModel> GetAsync(CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(Next);
        }
    }

    private sealed class FakeNotifier : IHealthAlertNotifier
    {
        public List<HealthAlertMessage> Sent { get; } = [];

        public HttpStatusCode? FailWith { get; set; }

        public string Channel => "discord";

        public Task SendAsync(HealthAlertMessage message, CancellationToken ct)
        {
            if (FailWith is { } status)
            {
                throw new HttpRequestException("refused", null, status);
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>The Mongo store's contract in memory: a versioned state round-tripped through JSON, and an alert log.</summary>
    private sealed class FakeStore : IHealthAlertStore
    {
        private string? _state;

        public List<HealthAlertDocument> Alerts { get; } = [];

        public bool LoseNextSave { get; set; }

        public bool IsActive => true;

        public Task<HealthAlertStateDocument?> GetStateAsync(CancellationToken ct)
            => Task.FromResult(_state is null ? null : JsonSerializer.Deserialize<HealthAlertStateDocument>(_state));

        public async Task<bool> TrySaveStateAsync(HealthAlertStateDocument state, long expectedVersion, CancellationToken ct)
        {
            var stored = await GetStateAsync(ct);
            if (LoseNextSave || (stored?.Version ?? 0) != expectedVersion)
            {
                LoseNextSave = false;
                return false;
            }

            state.Version = expectedVersion + 1;
            _state = JsonSerializer.Serialize(state);
            return true;
        }

        public Task RecordAlertAsync(HealthAlertDocument alert, CancellationToken ct)
        {
            Alerts.Add(alert);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<HealthAlertDocument>> GetRecentAlertsAsync(int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<HealthAlertDocument>>([.. Alerts.AsEnumerable().Reverse().Take(limit)]);
    }
}
