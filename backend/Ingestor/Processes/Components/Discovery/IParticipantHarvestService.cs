using Data.Repositories;
using Ingestor.Options;

namespace Ingestor.Processes.Components.Discovery;

public interface IParticipantHarvestService
{
    Task<HarvestResult> HarvestAsync(
        IDataSession session,
        HarvestOptions options,
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
public sealed record HarvestResult(int CandidatesInserted, int CandidatesUpdated, int AccountsCreated);
