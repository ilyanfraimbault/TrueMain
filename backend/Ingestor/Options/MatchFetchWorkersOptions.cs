using System.ComponentModel.DataAnnotations;

namespace Ingestor.Options;

/// <summary>
/// The permanent match-fetch workers of <see cref="JobMode.FetchWorkers"/> (#1457): one per
/// regional routing value, each claiming, fetching and writing its platforms' accounts
/// continuously while the rest of the fetch lane runs beside it on its own cadence.
/// </summary>
/// <remarks>
/// What each worker fetches per account is <see cref="MatchIngestionOptions"/>'s business, the
/// same as under <see cref="JobMode.FetchLane"/>: these options only say how often it claims.
/// </remarks>
public class MatchFetchWorkersOptions
{
    public const string SectionName = "MatchFetchWorkers";

    /// <summary>
    /// The most Riot calls a worker makes per minute on its regional route — the target rate
    /// the throughput is raised by, in stages (#1459). Enforced by the rate limiter, below
    /// Riot's own budget and beside the shares reserved for the other regional callers.
    /// </summary>
    /// <remarks>
    /// The default is what <c>MatchIngestion</c> averaged per regional host on production over
    /// the week before the workers shipped, measured over wall clock from the per-minute
    /// rollups, so the change of topology alone does not change the throughput. A personal key allows 47.5 a minute after the limiter's headroom, and 44
    /// once the default reservations are held back: that is the ceiling of the ramp, not a
    /// value to start from.
    /// </remarks>
    [Range(1, 100_000)]
    public int TargetRequestsPerMinute { get; set; } = 30;

    /// <summary>
    /// Accounts a worker claims at a time. Unset, it is <c>MatchIngestion:BatchSize</c> split
    /// evenly across the routing values, which is what each region took per pass when the
    /// lane claimed one batch for all of them.
    /// </summary>
    [Range(1, 1000)]
    public int? BatchSize { get; set; }

    /// <summary>How long a worker waits after a claim that found nothing to fetch, in seconds.</summary>
    [Range(1, 3600)]
    public int IdleDelaySeconds { get; set; } = 60;

    /// <summary>How long a worker waits after a batch that failed outright, in seconds.</summary>
    [Range(1, 3600)]
    public int FailureBackoffSeconds { get; set; } = 30;
}
