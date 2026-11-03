namespace Data.Ops.Mongo;

/// <summary>
/// The health alerter's persistence (#2292): its one state document and the log of the alerts
/// it sent. Degrades like the observability stores when Mongo is not configured — reads come
/// back empty, writes are no-ops — so the alerter still runs on its in-memory state.
/// </summary>
public interface IHealthAlertStore
{
    /// <summary>True when Mongo is configured, so the state survives a restart.</summary>
    bool IsActive { get; }

    /// <summary>The saved state, or null when none was ever saved (or Mongo is not configured).</summary>
    Task<HealthAlertStateDocument?> GetStateAsync(CancellationToken ct);

    /// <summary>
    /// Saves <paramref name="state"/> if the stored one still has <paramref name="expectedVersion"/>
    /// (0 for "none saved yet"), stamping it <c>expectedVersion + 1</c>. False when another
    /// writer saved first: the caller lost the race and must not act on its evaluation.
    /// </summary>
    Task<bool> TrySaveStateAsync(HealthAlertStateDocument state, long expectedVersion, CancellationToken ct);

    /// <summary>Appends one sent alert.</summary>
    Task RecordAlertAsync(HealthAlertDocument alert, CancellationToken ct);

    /// <summary>The newest <paramref name="limit"/> alerts, newest first.</summary>
    Task<IReadOnlyList<HealthAlertDocument>> GetRecentAlertsAsync(int limit, CancellationToken ct);
}
