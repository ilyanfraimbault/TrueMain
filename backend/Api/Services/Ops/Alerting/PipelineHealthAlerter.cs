using Data.Ops.Mongo;
using Microsoft.Extensions.Options;
using TrueMain.Options;

namespace TrueMain.Services.Ops.Alerting;

/// <summary>
/// Tells the operator when the pipeline-health verdict turns red, and when it recovers (#2292),
/// instead of waiting for someone to open <c>/health</c>.
///
/// <para>
/// Every <see cref="AlertingOptions.EvaluationInterval"/> it reads the cockpit payload, applies
/// <see cref="HealthAlertTransitions"/> to the saved state, and sends one message for whatever
/// changed — nothing when nothing did. The state lives in Mongo, read back on every pass, so a
/// deploy neither re-announces an incident nor forgets an open one, and a second Api container
/// sees the first one's writes.
/// </para>
///
/// <para>
/// <b>One sender per transition.</b> The new state is saved <em>before</em> the message goes
/// out, conditioned on the version it was computed from: when two containers evaluate at once
/// (a rolling deploy), only the one whose save lands sends. An alert the channel refuses is
/// recorded as undelivered and its failure rolled back, so the next pass tries it again.
/// </para>
///
/// <para>Off — the loop returns at once — unless <see cref="AlertingOptions.Enabled"/>.</para>
/// </summary>
internal sealed class PipelineHealthAlerter(
    IOptions<AlertingOptions> options,
    IPipelineHealthSnapshotSource source,
    IHealthAlertStore store,
    IHealthAlertNotifier notifier,
    TimeProvider clock,
    ILogger<PipelineHealthAlerter> logger) : BackgroundService
{
    private readonly AlertingOptions _options = options.Value;

    // The state of a host without Mongo, and the fallback between a lost race and the next read.
    private HealthAlertStateDocument? _memoryState;
    private bool _observedSinceBoot;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Pipeline-health alerting is off: no Discord webhook is configured");
            return;
        }

        if (!store.IsActive)
        {
            logger.LogWarning(
                "Pipeline-health alerting runs without Mongo: its state is kept in memory and a restart forgets open incidents");
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(_options.EvaluationInterval, clock, stoppingToken);
                try
                {
                    await EvaluateOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Pipeline-health alert evaluation failed; the next one runs in {Interval}", _options.EvaluationInterval);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down.
        }
    }

    /// <summary>
    /// One pass: evaluate, decide, save, send. Returns the alert it sent, or null when nothing
    /// changed, the pass only observed, or another container claimed the transition.
    /// </summary>
    internal async Task<HealthAlertDocument?> EvaluateOnceAsync(CancellationToken ct)
    {
        var health = await source.GetAsync(ct);
        var previous = await store.GetStateAsync(ct) ?? _memoryState;
        var now = clock.GetUtcNow().UtcDateTime;
        var observeOnly = !_observedSinceBoot;

        var decision = HealthAlertTransitions.Decide(
            previous,
            HealthAlertTransitions.Observe(health, _options),
            now,
            _options,
            observeOnly);
        _observedSinceBoot = true;

        var expectedVersion = previous?.Version ?? 0;
        if (!await store.TrySaveStateAsync(decision.State, expectedVersion, ct))
        {
            logger.LogInformation("Another Api instance evaluated pipeline health first; this pass sends nothing");
            return null;
        }

        _memoryState = decision.State;
        if (decision.Changes.Count == 0)
        {
            return null;
        }

        var message = HealthAlertMessage.Compose(health, decision.Changes, _options);
        var alert = new HealthAlertDocument
        {
            SentAtUtc = now,
            Kind = message.Kind == HealthAlertChangeKind.Failing ? "failing" : "recovered",
            Title = message.Title,
            Verdict = message.Verdict,
            Channel = notifier.Channel,
            Changes = [.. message.Changes.Select(ToDocument)]
        };

        try
        {
            await notifier.SendAsync(message, ct);
            alert.Delivered = true;
            logger.LogInformation("Sent pipeline-health alert {AlertTitle}", message.Title);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            alert.Error = ex is HttpRequestException { StatusCode: { } status }
                ? $"HTTP {(int)status}"
                : ex.GetType().Name;
            logger.LogWarning("Pipeline-health alert {AlertTitle} was not delivered: {AlertError}", message.Title, alert.Error);
            await RollBackAsync(decision, ct);
        }

        await store.RecordAlertAsync(alert, ct);
        return alert;
    }

    private async Task RollBackAsync(HealthAlertDecision decision, CancellationToken ct)
    {
        HealthAlertTransitions.MarkUndelivered(decision.State, decision.Changes);
        if (!await store.TrySaveStateAsync(decision.State, decision.State.Version, ct))
        {
            logger.LogWarning("Could not roll back an undelivered pipeline-health alert: the state moved on");
        }
    }

    private static HealthAlertChangeDocument ToDocument(HealthAlertChange change) => new()
    {
        Key = change.Key,
        Title = change.Title,
        Change = change.Kind == HealthAlertChangeKind.Failing ? "failing" : "recovered",
        Status = change.Status,
        Headline = change.Headline
    };
}
