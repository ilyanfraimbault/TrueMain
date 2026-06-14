using Data.Entities;

namespace Data.Repositories;

public interface IMatchParticipantRepository
{
    Task<List<MatchParticipant>> GetByMatchIdAsync(string matchId, CancellationToken ct);
    Task<List<MatchParticipant>> GetByMatchIdsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct);
    Task<List<ParticipantRow>> GetRecentParticipantsAsync(string platformId, string puuid, int queueId, int take, CancellationToken ct);
    Task<Dictionary<AccountKey, List<ParticipantRow>>> GetRecentParticipantsByAccountsAsync(
        IReadOnlyCollection<AccountKey> accounts,
        int queueId,
        int take,
        CancellationToken ct);
    Task<Dictionary<PerkCatalogKey, int>> GetOrCreatePerkCatalogIdsAsync(IReadOnlyCollection<PerkCatalogKey> keys, CancellationToken ct);

    /// <summary>
    /// Aggregates orphan participant rows (<c>RiotAccountId IS NULL</c> — untracked
    /// players) grouped by (platform, puuid, champion) for matches started on/after
    /// <paramref name="sinceUtc"/>, gated on a minimum observed-games count. Near-zero
    /// cost: reads only data already persisted by match ingestion, makes no Riot API
    /// calls. Feeds the participant harvest candidate generator (#485).
    /// </summary>
    Task<List<HarvestedCandidateRow>> GetHarvestCandidatesAsync(
        IReadOnlyCollection<string> platformIds,
        int queueId,
        int minObservedGames,
        int maxRows,
        DateTime sinceUtc,
        CancellationToken ct);

    void AddRange(IEnumerable<MatchParticipant> participants);
    void AddPerkSelections(IEnumerable<ParticipantPerkSelection> selections);
}

public sealed record ParticipantRow(int ChampionId, string TeamPosition);

public sealed record HarvestedCandidateRow(
    string PlatformId,
    string Puuid,
    int ChampionId,
    int ObservedGames,
    int ObservedWins,
    DateTime LastSeenUtc);
