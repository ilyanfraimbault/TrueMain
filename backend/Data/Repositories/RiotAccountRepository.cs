using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class RiotAccountRepository(TrueMainDbContext db) : IRiotAccountRepository
{
    public Task<RiotAccount?> GetByPuuidAsync(string puuid, CancellationToken ct)
        => db.RiotAccounts.FirstOrDefaultAsync(a => a.Puuid == puuid, ct);

    public Task<RiotAccount?> GetByKeyAsync(string platformId, string puuid, CancellationToken ct)
        => db.RiotAccounts.FirstOrDefaultAsync(a => a.PlatformId == platformId && a.Puuid == puuid, ct);

    public async Task<Dictionary<AccountKey, RiotAccount>> GetByKeysAsync(
        IReadOnlyCollection<AccountKey> accounts,
        CancellationToken ct)
    {
        var result = new Dictionary<AccountKey, RiotAccount>();
        if (accounts.Count == 0)
        {
            return result;
        }

        foreach (var grouping in accounts
                     .Distinct()
                     .GroupBy(a => a.PlatformId, StringComparer.OrdinalIgnoreCase))
        {
            var platformId = grouping.Key;
            var puuids = grouping.Select(a => a.Puuid).Distinct(StringComparer.Ordinal).ToList();

            var riotAccounts = await db.RiotAccounts
                .Where(a => a.PlatformId == platformId && puuids.Contains(a.Puuid))
                .ToListAsync(ct);

            foreach (var account in riotAccounts)
            {
                result[new AccountKey(account.PlatformId, account.Puuid)] = account;
            }
        }

        return result;
    }

    public Task<bool> ExistsByPuuidAsync(string puuid, CancellationToken ct)
        => db.RiotAccounts.AnyAsync(a => a.Puuid == puuid, ct);

    public async Task<List<RiotAccount>> GetAccountsForRefreshAsync(int batchSize, CancellationToken ct)
    {
        // Fair-mix prioritization: target 75% truemains, 25% non-truemains
        // per batch. Truemains (accounts linked to a MainChampionStat with
        // IsMain=true) need fresher rank data because they back the public
        // sorted list (issue #86) and profile chart (issue #118). The 25%
        // budget for non-truemains prevents starvation under load. Quota
        // underflow in either bucket is rebalanced to the other so we
        // always fill the batch when work is available.
        var safe = Math.Max(1, batchSize);
        var truemainQuota = (int)Math.Ceiling(safe * 0.75d);

        var truemainKeys = db.MainChampionStats
            .AsNoTracking()
            .Where(s => s.IsMain)
            .Select(s => new { s.PlatformId, s.Puuid });

        var truemains = await db.RiotAccounts
            .Where(a => truemainKeys.Any(m => m.PlatformId == a.PlatformId && m.Puuid == a.Puuid))
            .OrderBy(a =>
                (string.IsNullOrEmpty(a.GameName) || string.IsNullOrEmpty(a.TagLine))
                    ? 0
                    : 1)
            .ThenBy(a => a.UpdatedAtUtc)
            .Take(truemainQuota)
            .ToListAsync(ct);

        var remaining = safe - truemains.Count;
        if (remaining <= 0)
        {
            return truemains;
        }

        var pickedIds = truemains.Select(a => a.Id).ToList();

        var others = await db.RiotAccounts
            .Where(a => !pickedIds.Contains(a.Id)
                        && !truemainKeys.Any(m => m.PlatformId == a.PlatformId && m.Puuid == a.Puuid))
            .OrderBy(a =>
                (string.IsNullOrEmpty(a.GameName) || string.IsNullOrEmpty(a.TagLine))
                    ? 0
                    : 1)
            .ThenBy(a => a.UpdatedAtUtc)
            .Take(remaining)
            .ToListAsync(ct);

        truemains.AddRange(others);
        return truemains;
    }

    public Task<List<AccountKey>> GetAccountsForMainAnalysisAsync(DateTime cutoff, int batchSize, CancellationToken ct)
    {
        var accounts =
            from account in db.RiotAccounts.AsNoTracking()
            join candidate in db.MainCandidates.AsNoTracking()
                on new { account.PlatformId, account.Puuid } equals new { candidate.PlatformId, candidate.Puuid }
            where candidate.Status == MainCandidateStatus.Validated
            select account;

        if (cutoff > DateTime.MinValue)
        {
            accounts = accounts.Where(a => a.LastMainCalcAtUtc == null || a.LastMainCalcAtUtc < cutoff);
        }

        return accounts
            .Distinct()
            .OrderBy(a => a.LastMainCalcAtUtc == null ? 0 : 1)
            .ThenBy(a => a.LastMainCalcAtUtc)
            .Take(Math.Max(1, batchSize))
            .Select(a => new AccountKey(a.PlatformId, a.Puuid))
            .ToListAsync(ct);
    }

    public async Task<List<AccountKey>> ClaimAccountsForMatchIngestAtomicallyAsync(
        IReadOnlyCollection<string> platforms,
        int batchSize,
        DateTime nowUtc,
        TimeSpan lease,
        CancellationToken ct)
    {
        var normalizedPlatforms = platforms
            .Where(platform => !string.IsNullOrWhiteSpace(platform))
            .Select(platform => platform.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedPlatforms.Length == 0)
        {
            return [];
        }

        var safeBatchSize = Math.Max(1, batchSize);
        var safeLease = lease > TimeSpan.Zero ? lease : TimeSpan.FromMinutes(30);
        var leaseCutoff = nowUtc - safeLease;

        var queuedAccounts = db.MainCandidates
            .AsNoTracking()
            .Where(candidate =>
                candidate.Status == MainCandidateStatus.Queued &&
                normalizedPlatforms.Contains(candidate.PlatformId))
            .Select(candidate => new { candidate.PlatformId, candidate.Puuid });

        var mainAccounts = db.MainChampionStats
            .AsNoTracking()
            .Where(stat =>
                stat.IsMain &&
                normalizedPlatforms.Contains(stat.PlatformId))
            .Select(stat => new { stat.PlatformId, stat.Puuid });

        var candidateAccounts = queuedAccounts.Union(mainAccounts);

        var claimableCandidates = await (
                from candidate in candidateAccounts
                join account in db.RiotAccounts.AsNoTracking()
                    on new { candidate.PlatformId, candidate.Puuid }
                    equals new { account.PlatformId, account.Puuid }
                where account.MatchIngestStatus == MatchIngestStatus.Idle
                      || (account.MatchIngestStatus == MatchIngestStatus.Processing
                          && account.MatchIngestClaimedAtUtc != null
                          && account.MatchIngestClaimedAtUtc < leaseCutoff)
                orderby account.LastMatchIngestAtUtc == null ? 0 : 1,
                    account.LastMatchIngestAtUtc
                select new AccountKey(candidate.PlatformId, candidate.Puuid)
            )
            .Take(Math.Max(safeBatchSize * 4, safeBatchSize))
            .ToListAsync(ct);

        var claimed = new List<AccountKey>();
        foreach (var candidate in claimableCandidates)
        {
            if (claimed.Count >= safeBatchSize)
            {
                break;
            }

            var updated = await db.RiotAccounts
                .Where(account => account.PlatformId == candidate.PlatformId && account.Puuid == candidate.Puuid)
                .Where(account =>
                    account.MatchIngestStatus == MatchIngestStatus.Idle
                    || (account.MatchIngestStatus == MatchIngestStatus.Processing
                        && account.MatchIngestClaimedAtUtc != null
                        && account.MatchIngestClaimedAtUtc < leaseCutoff))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(account => account.MatchIngestStatus, MatchIngestStatus.Processing)
                        .SetProperty(account => account.MatchIngestClaimedAtUtc, nowUtc),
                    ct);

            if (updated > 0)
            {
                claimed.Add(candidate);
            }
        }

        return claimed;
    }

    public Task<int> SetMatchIngestStatusAsync(string platformId, string puuid, MatchIngestStatus status, CancellationToken ct)
    {
        var query = db.RiotAccounts
            .Where(a => a.PlatformId == platformId && a.Puuid == puuid);

        if (status == MatchIngestStatus.Idle)
        {
            return query.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(a => a.MatchIngestStatus, status)
                    .SetProperty(a => a.MatchIngestClaimedAtUtc, (DateTime?)null),
                ct);
        }

        return query.ExecuteUpdateAsync(
            setters => setters.SetProperty(a => a.MatchIngestStatus, status),
            ct);
    }

    public Task UpdateLastMatchIngestAtAsync(string platformId, string puuid, DateTime atUtc, CancellationToken ct)
        => db.RiotAccounts
            .Where(a => a.PlatformId == platformId && a.Puuid == puuid)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(a => a.LastMatchIngestAtUtc, atUtc),
                ct);

    public void Add(RiotAccount account)
        => db.RiotAccounts.Add(account);
}
