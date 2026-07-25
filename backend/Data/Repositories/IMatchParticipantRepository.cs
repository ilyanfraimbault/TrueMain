using Data.Entities;

namespace Data.Repositories;

public interface IMatchParticipantRepository
{
    Task<List<MatchParticipant>> GetByMatchIdAsync(string matchId, CancellationToken ct);
    Task<List<MatchParticipant>> GetByMatchIdsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct);

    /// <summary>
    /// Fills <see cref="MatchParticipant.RiotAccountId"/> for the orphan rows
    /// (<c>RiotAccountId IS NULL</c>) belonging to <paramref name="puuid"/> across
    /// <paramref name="matchIds"/> in a single set-based <c>UPDATE</c> round trip.
    /// Returns the number of rows affected.
    /// </summary>
    Task<int> BackfillRiotAccountIdAsync(
        IReadOnlyCollection<string> matchIds,
        string puuid,
        Guid riotAccountId,
        CancellationToken ct);
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
    ///
    /// Every eligible pair is classified against <c>main_candidates</c> as brand-new or
    /// already-known (#495) so the caller can budget the two independently instead of
    /// letting the most-observed (already harvested) players monopolise a single top-N.
    /// <paramref name="maxRowsPerBucket"/> caps EACH class on EACH platform, so the
    /// returned set is a superset of any global top-N the caller may take of either
    /// class. The exact eligible totals ride along per platform
    /// (<see cref="HarvestCandidateBatch.Eligibility"/>) so truncation is reportable
    /// rather than silent.
    /// </summary>
    Task<HarvestCandidateBatch> GetHarvestCandidatesAsync(
        IReadOnlyCollection<string> platformIds,
        int queueId,
        int minObservedGames,
        int maxRowsPerBucket,
        DateTime sinceUtc,
        CancellationToken ct);

    void AddRange(IEnumerable<MatchParticipant> participants);
    void AddPerkSelections(IEnumerable<ParticipantPerkSelection> selections);
}

public sealed record ParticipantRow(int ChampionId, string TeamPosition);

/// <summary>
/// One eligible (platform, puuid, champion) pair with its observed sample.
/// <c>IsKnownCandidate</c> is <c>true</c> when a refreshable <see cref="MainCandidate"/>
/// already exists for it — the harvest would only refresh its observed stats — and
/// <c>false</c> for a pair that never produced a candidate, i.e. genuinely new discovery.
/// Pairs whose existing candidate is not refreshable at all (a ladder / manual-seed
/// candidate the harvest must not touch, or a Rejected one it must not resurrect) are
/// returned in neither class.
/// </summary>
public sealed record HarvestedCandidateRow(
    string PlatformId,
    string Puuid,
    int ChampionId,
    int ObservedGames,
    int ObservedWins,
    DateTime LastSeenUtc,
    bool IsKnownCandidate);

/// <summary>
/// <c>Rows</c> holds the candidate rows, capped per platform and per class (new / known).
/// It is not a final selection: the caller applies its own cross-platform budget on top.
/// <c>Eligibility</c> holds the exact eligible counts per harvested platform, computed
/// over the whole aggregate (NOT capped), so the caller can report how much of the pool a
/// run left behind instead of truncating silently.
/// </summary>
public sealed record HarvestCandidateBatch(
    IReadOnlyList<HarvestedCandidateRow> Rows,
    IReadOnlyList<HarvestPlatformEligibility> Eligibility)
{
    public static HarvestCandidateBatch Empty { get; } = new([], []);
}

/// <summary>
/// How many (puuid, champion) pairs qualified on one platform: <c>EligibleNew</c> for
/// pairs with no candidate yet, <c>EligibleKnown</c> for pairs whose harvest candidate
/// already exists. Both are exact — counted before any cap.
/// </summary>
public sealed record HarvestPlatformEligibility(
    string PlatformId,
    int EligibleNew,
    int EligibleKnown);
