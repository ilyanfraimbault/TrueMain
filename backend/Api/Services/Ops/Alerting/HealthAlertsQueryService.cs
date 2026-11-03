using Data.Ops.Mongo;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Alerting;

public interface IHealthAlertsQueryService
{
    Task<HealthAlertsReadModel> GetAsync(int? limit, CancellationToken ct);
}

/// <summary>The <c>/health</c> alerts panel (#2292): the alerter's settings, open incidents and last alerts.</summary>
internal sealed class HealthAlertsQueryService(
    IHealthAlertStore store,
    IHealthAlertNotifier notifier,
    IOptions<AlertingOptions> options) : IHealthAlertsQueryService
{
    internal const int DefaultLimit = 10;
    internal const int MaxLimit = 50;

    public async Task<HealthAlertsReadModel> GetAsync(int? limit, CancellationToken ct)
    {
        var settings = options.Value;
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var state = await store.GetStateAsync(ct);
        var alerts = await store.GetRecentAlertsAsync(take, ct);

        return new HealthAlertsReadModel
        {
            Enabled = settings.Enabled,
            Channel = settings.Enabled ? notifier.Channel : null,
            EvaluationIntervalSeconds = (int)settings.EvaluationInterval.TotalSeconds,
            RedConfirmations = settings.RedConfirmations,
            CooldownMinutes = (int)settings.Cooldown.TotalMinutes,
            ProcessFailureThreshold = settings.ProcessFailureThreshold,
            LastEvaluatedAtUtc = state?.LastEvaluatedAtUtc is { } evaluated
                ? DateTime.SpecifyKind(evaluated, DateTimeKind.Utc)
                : null,
            OpenIncidents = state is null
                ? []
                : [.. state.Tracked.Where(entry => entry.Open).Select(entry => entry.Key)],
            Alerts = [.. alerts.Select(ToReadModel)]
        };
    }

    private static HealthAlertReadModel ToReadModel(HealthAlertDocument alert) => new()
    {
        Id = alert.Id.ToString(),
        SentAtUtc = DateTime.SpecifyKind(alert.SentAtUtc, DateTimeKind.Utc),
        Kind = alert.Kind,
        Title = alert.Title,
        Verdict = alert.Verdict,
        Channel = alert.Channel,
        Delivered = alert.Delivered,
        Error = alert.Error,
        Changes =
        [
            .. alert.Changes.Select(change => new HealthAlertChangeReadModel
            {
                Key = change.Key,
                Title = change.Title,
                Change = change.Change,
                Status = change.Status,
                Headline = change.Headline
            })
        ]
    };
}
