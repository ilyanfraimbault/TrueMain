using Data.Repositories;
using Ingestor.Options;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.Discovery;

public interface ICandidateUpsertService
{
    Task<CandidateUpsertResult> UpsertAsync(
        IDataSession session,
        string platformId,
        string puuid,
        IReadOnlyCollection<RiotChampionMasteryDto> masteries,
        DiscoveryOptions options,
        DateTime nowUtc,
        CancellationToken ct);
}

public sealed record CandidateUpsertResult(int Inserted, int Updated);
