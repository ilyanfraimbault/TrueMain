namespace Data.Repositories;

/// <summary>Per-minute lead-vs-opponent totals contributed by one match.</summary>
public sealed record TimelineLeadContribution(
    int ChampionId,
    string TeamPosition,
    string Patch,
    int IntervalMinute,
    int Games,
    long GoldDiff,
    long CsDiff,
    long KillsDiff,
    long LevelDiff,
    long XpDiff,
    long DamageDiff);

/// <summary>Per-minute lead-spread variance moments contributed by one match.</summary>
public sealed record TimelineSigmaContribution(
    int QueueId,
    string Patch,
    int IntervalMinute,
    long N,
    double SumGold,
    double SumSqGold,
    double SumDmg,
    double SumSqDmg);

/// <summary>Per-event occurrence counts contributed by one match.</summary>
public sealed record TimelinePowerspikeEventContribution(
    int ChampionId,
    string TeamPosition,
    string Patch,
    string EventType,
    int RefId,
    int Games,
    long SumEventMinute);

/// <summary>The whole additive footprint one match's timeline leaves on the aggregates.</summary>
public sealed record TimelineAggregateContribution(
    IReadOnlyList<TimelineLeadContribution> Leads,
    IReadOnlyList<TimelineSigmaContribution> Sigmas,
    IReadOnlyList<TimelinePowerspikeEventContribution> Events);

public interface ITimelineAggregateRepository
{
    /// <summary>
    /// Folds a match's contribution into the lead / sigma / event aggregates with
    /// additive upserts. Runs as immediate SQL on the session connection, so it
    /// commits (or rolls back) with the enclosing ingestion transaction.
    /// </summary>
    Task ApplyAsync(TimelineAggregateContribution contribution, DateTime aggregatedAtUtc, CancellationToken ct);
}
