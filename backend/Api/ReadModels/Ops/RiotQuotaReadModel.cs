namespace TrueMain.ReadModels.Ops;

/// <summary>
/// Riot quota utilisation for the admin Riot API panel (#1458): one row per routing host
/// — Riot keeps one app budget per host — with its measured call rate against the limit
/// that host advertised, its 429 rate and who spent it, plus the duty cycle of each
/// ingestor lane over the same window.
/// </summary>
public sealed record RiotQuotaReadModel
{
    /// <summary>The resolved window key: <c>1h</c> / <c>24h</c> / <c>7d</c> / <c>30d</c>.</summary>
    public string Window { get; init; } = string.Empty;

    public DateTime SinceUtc { get; init; }

    public DateTime GeneratedAtUtc { get; init; }

    /// <summary>
    /// Where the rates are measured from: <see cref="SinceUtc"/>, or the oldest
    /// route-keyed rollup when the retention or the #1458 cutover ends inside the window
    /// (now, with <see cref="CoveredHours"/> at zero, when there is none yet). Every
    /// per-host rate divides by <see cref="CoveredHours"/>, never by the nominal window.
    /// </summary>
    public DateTime CoverageStartUtc { get; init; }

    public double CoveredHours { get; init; }

    /// <summary>The rollup retention the API is configured with, in days; null when the TTL is disabled.</summary>
    public double? RetentionDays { get; init; }

    /// <summary>The oldest rollup still stored; null when the collection is empty.</summary>
    public DateTime? OldestRetainedUtc { get; init; }

    /// <summary>Regional hosts first, then platform hosts, each by calls descending.</summary>
    public IReadOnlyList<RiotRouteQuotaReadModel> Routes { get; init; } = [];

    /// <summary>Lanes by duty cycle descending.</summary>
    public IReadOnlyList<LaneDutyCycleReadModel> Lanes { get; init; } = [];
}

/// <summary>
/// One routing host's budget. <see cref="Kind"/> is <c>regional</c> (americas, europe,
/// asia, sea — account-v1 and match-v5), <c>platform</c> (euw1, na1, kr… — summoner,
/// league, mastery) or <c>unknown</c>. The utilisation fields are null when the host
/// returned no parseable <c>X-App-Rate-Limit</c> in the window: without the advertised
/// limit there is nothing to divide by.
/// </summary>
public sealed record RiotRouteQuotaReadModel
{
    public string Route { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    /// <summary>Every physical attempt, 429s included — a rejected request still counted.</summary>
    public long Calls { get; init; }

    public long RateLimited { get; init; }

    /// <summary>429s / calls in [0, 1]; null when there were no calls.</summary>
    public double? RateLimitedRate { get; init; }

    public long Errors { get; init; }

    /// <summary>Calls per minute averaged over the covered span.</summary>
    public double CallsPerMinute { get; init; }

    /// <summary>Share of the covered span's minutes with at least one call to this host.</summary>
    public double ActiveMinuteShare { get; init; }

    /// <summary>The advertised window with the lowest sustained ceiling (same rule as the headroom estimate).</summary>
    public RiotApiBindingLimitReadModel? BindingLimit { get; init; }

    /// <summary>Calls over the covered span / what the binding limit allows over it.</summary>
    public double? Utilisation { get; init; }

    /// <summary>
    /// The freshest <c>X-App-Rate-Limit-Count</c> on the binding window divided by its
    /// limit — the instantaneous fill of the host's budget at <see cref="ObservedAtUtc"/>.
    /// </summary>
    public double? CurrentUtilisation { get; init; }

    public string? AppRateLimit { get; init; }

    public string? AppRateLimitCount { get; init; }

    public DateTime? ObservedAtUtc { get; init; }

    public IReadOnlyList<RiotRouteConsumerReadModel> Consumers { get; init; } = [];
}

/// <summary>One caller × endpoint spending a host's budget. <see cref="Share"/> is of the host's calls.</summary>
public sealed record RiotRouteConsumerReadModel
{
    public string Caller { get; init; } = string.Empty;

    public string Endpoint { get; init; } = string.Empty;

    public long Calls { get; init; }

    public long RateLimited { get; init; }

    public double Share { get; init; }
}

/// <summary>
/// The share of wall-clock time a lane (the job mode a host ran as) had at least one
/// process running over the window, from the recorded process runs. <c>unassigned</c>
/// gathers runs recorded without a job mode.
/// </summary>
public sealed record LaneDutyCycleReadModel
{
    public string Lane { get; init; } = string.Empty;

    /// <summary>Busy time / window duration, in [0, 1].</summary>
    public double DutyCycle { get; init; }

    public double BusyHours { get; init; }

    public int Runs { get; init; }

    /// <summary>The lane's processes by duty cycle descending.</summary>
    public IReadOnlyList<ProcessDutyCycleReadModel> Processes { get; init; } = [];
}

public sealed record ProcessDutyCycleReadModel
{
    public string ProcessName { get; init; } = string.Empty;

    public double DutyCycle { get; init; }

    public double BusyHours { get; init; }

    public int Runs { get; init; }
}
