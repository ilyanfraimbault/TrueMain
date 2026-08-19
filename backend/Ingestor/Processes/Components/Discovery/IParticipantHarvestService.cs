using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Components.Coverage;

namespace Ingestor.Processes.Components.Discovery;

public interface IParticipantHarvestService
{
    Task<HarvestResult> HarvestAsync(
        IDataSession session,
        HarvestOptions options,
        ChampionCoverageSnapshot coverage,
        DateTime nowUtc,
        CancellationToken ct);
}

/// <param name="CandidatesInserted">New harvest candidates added this run.</param>
/// <param name="CandidatesUpdated">
/// Existing harvest candidates whose observed stats were refreshed. This counts ALL stat
/// refreshes, not just re-queues: a Scored candidate reset to New, but also in-flight
/// (Queued/Processing), Validated and Rejected candidates whose stats moved without any
/// status change. So <c>candidatesUpdated</c> in the ops log is "rows touched", not "rows
/// that changed pipeline state" — split into a StatsOnly outcome if that distinction is
/// ever needed for monitoring.
/// </param>
/// <param name="AccountsCreated">Minimal RiotAccount rows created for unknown puuids.</param>
/// <param name="Coverage">How much of the eligible pool the run's budget actually covered.</param>
public sealed record HarvestResult(
    int CandidatesInserted,
    int CandidatesUpdated,
    int AccountsCreated,
    HarvestCoverage Coverage);

/// <summary>
/// What a harvest run selected out of what qualified (#495). The harvest budget
/// (<c>Harvest:MaxCandidatesPerRun</c>) is smaller than the eligible pool as soon as the
/// orphan population grows, so every run drops rows; this makes the drop explicit and
/// reportable instead of an invisible <c>LIMIT</c>. Counted separately for pairs with no
/// candidate yet (new discovery — the arm that starves) and pairs whose candidate only
/// needs its observed stats refreshed. <c>Platforms</c> repeats the same split per
/// platform, which also exposes an imbalanced run (one region eating a cross-platform
/// budget).
/// </summary>
public sealed record HarvestCoverage(
    int EligibleNew,
    int SelectedNew,
    int EligibleKnown,
    int SelectedKnown,
    IReadOnlyList<HarvestPlatformCoverage> Platforms)
{
    public static HarvestCoverage Empty { get; } = new(0, 0, 0, 0, []);

    /// <summary>Eligible new pairs left for a later run — the starvation signal.</summary>
    public int DroppedNew => EligibleNew - SelectedNew;

    /// <summary>Existing candidates whose observed stats stayed stale this run.</summary>
    public int DroppedKnown => EligibleKnown - SelectedKnown;

    /// <summary>True when the budget, not the data, decided where the run stopped.</summary>
    public bool IsBudgetBound => DroppedNew > 0 || DroppedKnown > 0;
}

/// <inheritdoc cref="HarvestCoverage"/>
public sealed record HarvestPlatformCoverage(
    string PlatformId,
    int EligibleNew,
    int SelectedNew,
    int EligibleKnown,
    int SelectedKnown);
