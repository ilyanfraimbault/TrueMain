using Data.Entities;

namespace Data.Repositories;

public interface IMatchParticipantRepository
{
    Task<List<MatchParticipant>> GetByMatchIdAsync(string matchId, CancellationToken ct);
    Task<List<MatchParticipant>> GetByMatchIdsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct);
    Task<List<ParticipantRow>> GetRecentParticipantsAsync(string platformId, string puuid, int queueId, int take, CancellationToken ct);
    Task<Dictionary<PerkCatalogKey, int>> GetOrCreatePerkCatalogIdsAsync(IReadOnlyCollection<PerkCatalogKey> keys, CancellationToken ct);
    void AddRange(IEnumerable<MatchParticipant> participants);
    void AddPerkSelections(IEnumerable<ParticipantPerkSelection> selections);
}

public sealed record ParticipantRow(int ChampionId, string TeamPosition);
