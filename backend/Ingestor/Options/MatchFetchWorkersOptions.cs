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
    /// The target rates the throughput is raised through, one stage at a time (#1459): the
    /// most Riot calls a worker makes per minute on its regional route at each stage, in
    /// ascending order. Enforced by the rate limiter, below Riot's own budget and beside the
    /// shares reserved for the other regional callers.
    /// </summary>
    /// <remarks>
    /// The first stage is what <c>MatchIngestion</c> averaged per regional host on production
    /// over the week before the workers shipped (#1457), measured over wall clock from the
    /// per-minute rollups, so the topology change alone did not change the throughput. The
    /// last is the ceiling of a personal key: 47.5 calls a minute after the limiter's
    /// headroom, 44 once the default reservations are held back. A production key's budget
    /// is far larger, and its ramp is a new list, not a longer one.
    /// <para>
    /// Empty in the class and filled with <see cref="DefaultRampStages"/> after binding when
    /// nothing configures it: the configuration binder appends to a list that already holds
    /// items, so a configured list would otherwise land after the defaults instead of
    /// replacing them.
    /// </para>
    /// </remarks>
    public List<int> RampStages { get; set; } = [];

    /// <summary>The stages used when <see cref="RampStages"/> is not configured.</summary>
    public static IReadOnlyList<int> DefaultRampStages { get; } = [30, 36, 40, 44];

    /// <summary>
    /// The stage of <see cref="RampStages"/> the workers run at, counted from 1. Raised one
    /// step at a time, each held long enough to measure (<c>docs/ingestion-ramp.md</c>).
    /// </summary>
    [Range(1, 100)]
    public int RampStage { get; set; } = 1;

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

    /// <summary>The target rate of the configured stage, in Riot calls a minute per regional route.</summary>
    /// <remarks>
    /// Falls back to <see cref="DefaultRampStages"/> on its own for an instance built without
    /// the post-binding fill, as tests and hand-built options are.
    /// </remarks>
    public int TargetRequestsPerMinute() => (RampStages.Count > 0 ? RampStages : DefaultRampStages)[RampStage - 1];
}
