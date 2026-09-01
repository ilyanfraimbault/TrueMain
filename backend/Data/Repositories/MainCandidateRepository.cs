using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class MainCandidateRepository(TrueMainDbContext db) : IMainCandidateRepository
{
    public Task<List<AccountKey>> GetQueuedAccountsAsync(List<string> platforms, CancellationToken ct)
    {
        return db.MainCandidates
            .AsNoTracking()
            .Where(c => c.Status == MainCandidateStatus.Queued && platforms.Contains(c.PlatformId))
            .GroupBy(c => new { c.PlatformId, c.Puuid })
            .Select(g => new AccountKey(g.Key.PlatformId, g.Key.Puuid))
            .ToListAsync(ct);
    }

    public Task<int> SetStatusForAccountAsync(string platformId, string puuid, MainCandidateStatus from, MainCandidateStatus to, CancellationToken ct)
    {
        return db.MainCandidates
            .Where(c => c.PlatformId == platformId && c.Puuid == puuid && c.Status == from)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, to), ct);
    }

    public Task<int> SetStatusForAccountAsync(string platformId, string puuid, IReadOnlyCollection<MainCandidateStatus> from, MainCandidateStatus to, CancellationToken ct)
    {
        return db.MainCandidates
            .Where(c => c.PlatformId == platformId && c.Puuid == puuid && from.Contains(c.Status))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, to), ct);
    }

    public async Task<IReadOnlyCollection<AccountKey>> SetStatusForAccountsAsync(
        IReadOnlyCollection<AccountKey> accounts,
        MainCandidateStatus from,
        MainCandidateStatus to,
        CancellationToken ct)
    {
        var affectedAccounts = new List<AccountKey>();
        if (accounts.Count == 0)
        {
            return affectedAccounts;
        }

        // Grouped by platform, mirroring RiotAccountRepository.GetByKeysAsync: the
        // round-trip count depends on the number of distinct platforms in the
        // batch (a handful in practice — KR/EUW1/NA1), not on the number of
        // accounts (#858).
        foreach (var grouping in accounts
                     .Distinct()
                     .GroupBy(a => a.PlatformId, StringComparer.OrdinalIgnoreCase))
        {
            var platformId = grouping.Key;
            var puuids = grouping.Select(a => a.Puuid).Distinct(StringComparer.Ordinal).ToList();

            // Read which of the requested accounts actually have a `from` row
            // before mutating, so the caller learns exactly which accounts were
            // affected — an ExecuteUpdate row count would over-count an account
            // that carries several candidate rows (one per champion).
            //
            // The SELECT and the ExecuteUpdate below are two round-trips, not one
            // atomic operation: a row could in theory change status between them,
            // which would make affectedPuuids report an account as "affected"
            // without the UPDATE having actually touched it. This is only safe
            // because the ingestor is a single instance running a strictly
            // sequential pipeline (see Worker.cs), so nothing else can write to
            // these rows between the two queries. If the ingestor is ever scaled
            // to multiple instances, this TOCTOU gap becomes a real bug and this
            // method would need a single atomic UPDATE ... RETURNING instead.
            var affectedPuuids = await db.MainCandidates
                .Where(c => c.PlatformId == platformId && puuids.Contains(c.Puuid) && c.Status == from)
                .Select(c => c.Puuid)
                .Distinct()
                .ToListAsync(ct);

            if (affectedPuuids.Count == 0)
            {
                continue;
            }

            await db.MainCandidates
                .Where(c => c.PlatformId == platformId && affectedPuuids.Contains(c.Puuid) && c.Status == from)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, to), ct);

            affectedAccounts.AddRange(affectedPuuids.Select(puuid => new AccountKey(platformId, puuid)));
        }

        return affectedAccounts;
    }

    public Task<int> MarkValidatedForAccountAsync(string platformId, string puuid, DateTime validatedAtUtc, CancellationToken ct)
    {
        return db.MainCandidates
            .Where(c => c.PlatformId == platformId && c.Puuid == puuid && c.Status == MainCandidateStatus.Processing)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(c => c.Status, MainCandidateStatus.Validated)
                    .SetProperty(c => c.ValidatedAtUtc, validatedAtUtc),
                ct);
    }

    public Task<List<MainCandidate>> GetByStatusAsync(MainCandidateStatus status, CancellationToken ct)
        => db.MainCandidates.AsNoTracking().Where(c => c.Status == status).ToListAsync(ct);

    public Task<List<MainCandidate>> GetNewBatchAsync(int batchSize, CancellationToken ct)
        => db.MainCandidates
            .Where(c => c.Status == MainCandidateStatus.New)
            .OrderBy(c => c.Id)
            .Take(Math.Max(1, batchSize))
            .ToListAsync(ct);

    public Task<List<MainCandidate>> GetByPlatformPuuidAndChampionsAsync(string platformId, string puuid, List<int> championIds, CancellationToken ct)
        => db.MainCandidates
            .Where(c => c.PlatformId == platformId && c.Puuid == puuid && championIds.Contains(c.ChampionId))
            .ToListAsync(ct);

    public Task<List<MainCandidate>> GetScoredByPlatformAsync(
        string platformId,
        int take,
        IReadOnlyCollection<int> deprioritizedChampionIds,
        CancellationToken ct)
        => db.MainCandidates
            .Where(c => c.PlatformId == platformId && c.Status == MainCandidateStatus.Scored)
            // Champions that already reached the coverage target sort last whatever their
            // score (#900), so they only take the slots an under-covered champion left free.
            .OrderBy(c => deprioritizedChampionIds.Contains(c.ChampionId) ? 1 : 0)
            .ThenByDescending(c => c.Score)
            .ThenBy(c => c.ScoredAtUtc == null ? 0 : 1)
            .ThenBy(c => c.ScoredAtUtc)
            .ThenBy(c => c.Id)
            .Take(Math.Max(0, take))
            .ToListAsync(ct);

    public Task<List<MainCandidate>> GetByPlatformsAndPuuidsAsync(
        IReadOnlyCollection<string> platformIds,
        IReadOnlyCollection<string> puuids,
        CancellationToken ct)
    {
        if (platformIds.Count == 0 || puuids.Count == 0)
        {
            return Task.FromResult(new List<MainCandidate>());
        }

        var platformArray = platformIds.ToArray();
        var puuidArray = puuids.ToArray();

        // Cartesian filter (platform IN ... AND puuid IN ...), not exact (platform, puuid)
        // pair matching — a deliberate trade-off. Riot puuids are globally unique, so a puuid
        // never recurs under another platform and no spurious cross-platform rows are loaded;
        // even if the data ever diverged, the caller keys on the exact (platform, puuid,
        // champion) tuple, so extras are ignored. Keeps the query a single index-friendly IN.
        return db.MainCandidates
            .Where(c => platformArray.Contains(c.PlatformId) && puuidArray.Contains(c.Puuid))
            .ToListAsync(ct);
    }

    private static readonly MainCandidateStatus[] NeverPromotedStatuses =
        [MainCandidateStatus.New, MainCandidateStatus.Scored, MainCandidateStatus.Rejected];

    public Task<int> PruneStaleNeverPromotedAsync(DateTime lastPlayCutoffUtc, CancellationToken ct)
        => db.MainCandidates
            .Where(c => NeverPromotedStatuses.Contains(c.Status)
                        && c.ValidatedAtUtc == null
                        && c.LastPlayTimeUtc < lastPlayCutoffUtc)
            .ExecuteDeleteAsync(ct);

    public Task<int> ReleaseExpiredClaimsAsync(DateTime leaseCutoffUtc, CancellationToken ct)
        => db.MainCandidates
            .Where(c => c.Status == MainCandidateStatus.Processing
                        // Negated on purpose: "no live claim behind this row", not "an expired
                        // one in front of it". A candidate whose account row is gone has no
                        // claim either, and an EXISTS on the expired shape would leave it
                        // Processing for good.
                        && !db.RiotAccounts.Any(a => a.PlatformId == c.PlatformId
                                                     && a.Puuid == c.Puuid
                                                     && a.MatchIngestStatus == MatchIngestStatus.Processing
                                                     && a.MatchIngestClaimedAtUtc != null
                                                     && a.MatchIngestClaimedAtUtc >= leaseCutoffUtc))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, MainCandidateStatus.Queued), ct);

    public void Add(MainCandidate candidate)
        => db.MainCandidates.Add(candidate);
}
