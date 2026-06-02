using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class RiotAccountRepository(TrueMainDbContext db) : IRiotAccountRepository
{
    public Task<RiotAccount?> GetByPuuidAsync(string puuid, CancellationToken ct)
        => db.RiotAccounts.FirstOrDefaultAsync(a => a.Puuid == puuid, ct);

    public Task<RiotAccount?> GetByKeyAsync(string platformId, string puuid, CancellationToken ct)
        => db.RiotAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.PlatformId == platformId && a.Puuid == puuid, ct);

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
        // Two-stage prioritisation:
        //
        // Priority 0 (no quota): every truemain whose identity is incomplete
        //   (GameName or TagLine empty). These back the public surfaces
        //   (/truemains, /profile) and AccountRefresh is the only writer for
        //   that identity (account-v1). When the backlog is large — e.g.
        //   1514 accounts after the #182 clobber fix shipped — the entire
        //   batch goes to draining it.
        //
        // Priority 1 (75 % truemain / 25 % non-truemain fair-mix): applied to
        //   whatever capacity remains after priority 0. Truemains still
        //   matter more because they back public surfaces (#86, #118); the
        //   25 % budget for non-truemains prevents starvation. Quota
        //   underflow in either P1 bucket is rebalanced to the other so we
        //   always fill the batch when work is available.
        var safe = Math.Max(1, batchSize);

        var truemainKeys = db.MainChampionStats
            .AsNoTracking()
            .Where(s => s.IsMain)
            .Select(s => new { s.PlatformId, s.Puuid });

        // Rank-score ordering (#194) is scoped to the P1 truemain bucket only.
        // Within that bucket, after the identity-missing prefix (#188), accounts
        // are ordered by:
        //   1. identity-missing prefix (incomplete GameName/TagLine first, #188)
        //   2. rank score DESCENDING, NULLS LAST — Challenger > … > Iron IV,
        //      unranked / no-snapshot accounts (Score == null) sort last so they
        //      stay eligible but yield to ranked accounts.
        //   3. UpdatedAtUtc ASCENDING (final tiebreaker, prevents starvation).
        // The score is the denormalised riot_accounts."Score" column, kept in
        // lock-step with each account's latest rank by the rank ingestion writer
        // (Ingestor.Ranking.RankSnapshotWriter -> Core.Lol.Ranking.RankScore —
        // the single source of truth for the CASE coefficients). The Data layer
        // only reads it, so there is no Data -> Api/Core dependency and no inline
        // score CASE here.
        // EF's ThenByDescending on a nullable column emits plain DESC, which on
        // Postgres sorts NULLs FIRST; the leading `a.Score == null` key flips
        // that to NULLS LAST without needing a raw NULLS LAST clause.
        // Priority 0 (incomplete-truemain backlog, #188) and the P1 non-truemain
        // bucket keep their UpdatedAtUtc oldest-first drain order and are NOT
        // reordered by score.

        // ── Priority 0 ───────────────────────────────────────────────────
        // Drain the identity backlog oldest-first (#188). Every row here is
        // identity-incomplete, so score is intentionally not a sort key — a
        // recently-updated high-rank account must not jump ahead of older ones.
        var incompleteTruemains = await db.RiotAccounts
            .Where(a => (string.IsNullOrEmpty(a.GameName) || string.IsNullOrEmpty(a.TagLine))
                        && truemainKeys.Any(m => m.PlatformId == a.PlatformId && m.Puuid == a.Puuid))
            .OrderBy(a => a.UpdatedAtUtc)
            .Take(safe)
            .ToListAsync(ct);

        var remaining = safe - incompleteTruemains.Count;
        if (remaining <= 0)
        {
            return incompleteTruemains;
        }

        // ── Priority 1: 75 % truemains ───────────────────────────────────
        var pickedIds = incompleteTruemains.Select(a => a.Id).ToHashSet();
        var truemainQuota = (int)Math.Ceiling(remaining * 0.75d);

        var truemains = await db.RiotAccounts
            .Where(a => !pickedIds.Contains(a.Id)
                        && truemainKeys.Any(m => m.PlatformId == a.PlatformId && m.Puuid == a.Puuid))
            .OrderBy(a =>
                (string.IsNullOrEmpty(a.GameName) || string.IsNullOrEmpty(a.TagLine))
                    ? 0
                    : 1)
            .ThenBy(a => a.Score == null)
            .ThenByDescending(a => a.Score)
            .ThenBy(a => a.UpdatedAtUtc)
            .Take(truemainQuota)
            .ToListAsync(ct);

        foreach (var picked in truemains)
        {
            pickedIds.Add(picked.Id);
        }

        var leftover = remaining - truemains.Count;
        if (leftover <= 0)
        {
            incompleteTruemains.AddRange(truemains);
            return incompleteTruemains;
        }

        // ── Priority 1: 25 % non-truemains (absorbs any truemain underflow) ─
        // Not the truemain bucket: keep identity-missing-first then oldest-first
        // (#188). Score ordering (#194) is intentionally scoped to truemains only.
        var others = await db.RiotAccounts
            .Where(a => !pickedIds.Contains(a.Id)
                        && !truemainKeys.Any(m => m.PlatformId == a.PlatformId && m.Puuid == a.Puuid))
            .OrderBy(a =>
                (string.IsNullOrEmpty(a.GameName) || string.IsNullOrEmpty(a.TagLine))
                    ? 0
                    : 1)
            .ThenBy(a => a.UpdatedAtUtc)
            .Take(leftover)
            .ToListAsync(ct);

        incompleteTruemains.AddRange(truemains);
        incompleteTruemains.AddRange(others);
        return incompleteTruemains;
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
