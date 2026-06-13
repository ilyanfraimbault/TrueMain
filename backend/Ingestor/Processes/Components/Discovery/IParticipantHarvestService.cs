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

public sealed record HarvestResult(int CandidatesInserted, int CandidatesUpdated, int AccountsCreated);
