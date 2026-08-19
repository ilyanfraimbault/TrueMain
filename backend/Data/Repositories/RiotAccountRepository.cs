using System.Linq.Expressions;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class RiotAccountRepository(TrueMainDbContext db) : IRiotAccountRepository
{
    public Task<RiotAccount?> GetByPuuidAsync(string puuid, CancellationToken ct)
        => db.RiotAccounts.FirstOrDefaultAsync(a => a.Puuid == puuid, ct);

    public Task<RiotAccount?> GetByKeyAsync(string platformId, string puuid, CancellationToken ct)
        => db.RiotAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.PlatformId == platformId && a.Puuid == puuid, ct);

    public async Task<HashSet<string>> GetExistingPuuidsAsync(IReadOnlyCollection<string> puuids, CancellationToken ct)
    {
        if (puuids.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var puuidArray = puuids.Distinct(StringComparer.Ordinal).ToArray();
        var found = await db.RiotAccounts
            .AsNoTracking()
            .Where(a => puuidArray.Contains(a.Puuid))
            .Select(a => a.Puuid)
            .ToListAsync(ct);

        return found.ToHashSet(StringComparer.Ordinal);
    }

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
        // Prioritisation, in order:
        //
        // Priority 0 (incomplete-identity truemains — no quota): every truemain
        //   whose identity is incomplete (GameName or TagLine empty). These back
        //   the public surfaces (/truemains, /profile) and AccountRefresh is the
        //   only writer for that identity (account-v1). When the backlog is large
        //   the entire batch goes to draining it.
        //
        // Priority 0.5 (incomplete-identity non-truemains — capped at 50 %):
        //   match discovery inserts PUUID-only accounts continuously
        //   (ParticipantHarvestService), almost all non-truemains; before #788
        //   they were confined to the 25 % P1 non-truemain share and the backlog
        //   never drained. An account with no Riot ID also cannot be recovered
        //   once its PUUID stops resolving — a rotated PUUID / API-key change
        //   404s and the row is marked Invalid (#785/#787) — so backfilling it is
        //   high value. Capped at half the remaining batch so it can never fully
        //   starve truemain rank refresh.
        //
        // Priority 1 (75 % truemain / 25 % non-truemain fair-mix): applied to
        //   whatever capacity remains after the identity buckets. Truemains still
        //   matter more because they back public surfaces (#86, #118); the 25 %
        //   budget for non-truemains prevents starvation. Quota underflow in
        //   either P1 bucket is rebalanced to the other so we always fill the
        //   batch when work is available.
        var safe = Math.Max(1, batchSize);

        var truemainKeys = db.MainChampionStats
            .AsNoTracking()
            .Where(s => s.IsMain && s.IsActive)
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
        // The identity-backfill buckets (P0/P0.5, #188/#788) and the P1
        // non-truemain bucket keep their UpdatedAtUtc oldest-first drain order
        // and are NOT reordered by score.

        // ── Priority 0: incomplete-identity truemains (no quota) ─────────
        // Drain the truemain identity backlog oldest-first (#188). Every row
        // here is identity-incomplete, so score is intentionally not a sort key
        // — a recently-updated high-rank account must not jump ahead of older
        // ones.
        var selected = await db.RiotAccounts
            .Where(a => a.Status == RiotAccountStatus.Active
                        && (string.IsNullOrEmpty(a.GameName) || string.IsNullOrEmpty(a.TagLine))
                        && truemainKeys.Any(m => m.PlatformId == a.PlatformId && m.Puuid == a.Puuid))
            .OrderBy(a => a.UpdatedAtUtc)
            .Take(safe)
            .ToListAsync(ct);

        var remaining = safe - selected.Count;
        if (remaining <= 0)
        {
            return selected;
        }

        var pickedIds = selected.Select(a => a.Id).ToHashSet();

        // ── Priority 0.5: incomplete-identity non-truemains (capped 50 %) ─
        // #788: give match-discovered PUUID-only accounts a dedicated share so
        // they are not confined to the 25 % P1 bucket and can actually keep up
        // with discovery inflow. Capped at half the remaining batch so a large
        // backlog can't starve truemain refresh. Oldest-first (#188).
        var identityQuota = Math.Max(1, (int)Math.Ceiling(remaining * 0.5d));
        var incompleteOthers = await db.RiotAccounts
            .Where(a => a.Status == RiotAccountStatus.Active
                        && (string.IsNullOrEmpty(a.GameName) || string.IsNullOrEmpty(a.TagLine))
                        && !truemainKeys.Any(m => m.PlatformId == a.PlatformId && m.Puuid == a.Puuid))
            .OrderBy(a => a.UpdatedAtUtc)
            .Take(identityQuota)
            .ToListAsync(ct);

        foreach (var account in incompleteOthers)
        {
            pickedIds.Add(account.Id);
        }

        selected.AddRange(incompleteOthers);
        remaining -= incompleteOthers.Count;
        if (remaining <= 0)
        {
            return selected;
        }

        // ── Priority 1: 75 % truemains ───────────────────────────────────
        var truemainQuota = (int)Math.Ceiling(remaining * 0.75d);

        var truemains = await db.RiotAccounts
            .Where(a => a.Status == RiotAccountStatus.Active
                        && !pickedIds.Contains(a.Id)
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
            selected.AddRange(truemains);
            return selected;
        }

        // ── Priority 1: 25 % non-truemains (absorbs any truemain underflow) ─
        // Not the truemain bucket: keep identity-missing-first then oldest-first
        // (#188). Score ordering (#194) is intentionally scoped to truemains only.
        var others = await db.RiotAccounts
            .Where(a => a.Status == RiotAccountStatus.Active
                        && !pickedIds.Contains(a.Id)
                        && !truemainKeys.Any(m => m.PlatformId == a.PlatformId && m.Puuid == a.Puuid))
            .OrderBy(a =>
                (string.IsNullOrEmpty(a.GameName) || string.IsNullOrEmpty(a.TagLine))
                    ? 0
                    : 1)
            .ThenBy(a => a.UpdatedAtUtc)
            .Take(leftover)
            .ToListAsync(ct);

        selected.AddRange(truemains);
        selected.AddRange(others);
        return selected;
    }

    public Task<List<AccountKey>> GetAccountsForMainAnalysisAsync(DateTime cutoff, int batchSize, CancellationToken ct)
    {
        // Two eligibility paths, OR'd via EXISTS so each account appears once:
        //
        //  1. A Validated main candidate — the first-time path. A candidate is
        //     validated through the account's OWN MatchIngestion run.
        //  2. An account that ALREADY has an established main
        //     (main_champion_stats with IsMain). The per-account match-ingest
        //     queue is heavily backlogged (ordered by oldest LastMatchIngestAtUtc),
        //     so a validated candidate can sit un-reprocessed for months while the
        //     account keeps accruing recent participants harvested passively from
        //     other tracked players' games. Without this path the displayed main
        //     freezes at whatever the last own-ingest computed (#825). Re-including
        //     established mains lets MainAnalysis refresh them from their recent
        //     games regardless of candidate status; the thin-sample guard in
        //     MainAnalysisProcess prevents a small passive sample from wiping a
        //     main it can't reclassify.
        //
        // EXISTS (not a join) keeps one row per account, so the old Distinct is
        // unnecessary. Cutoff + ordering + Take throttle the set identically to
        // before, so the added path can't blow up per-cycle load.
        var accounts =
            from account in db.RiotAccounts.AsNoTracking()
            where account.Status == RiotAccountStatus.Active
                  && (db.MainCandidates.Any(candidate =>
                          candidate.PlatformId == account.PlatformId
                          && candidate.Puuid == account.Puuid
                          && candidate.Status == MainCandidateStatus.Validated)
                      || db.MainChampionStats.Any(stat =>
                          stat.PlatformId == account.PlatformId
                          && stat.Puuid == account.Puuid
                          && stat.IsMain
                          && stat.IsActive))
            select account;

        if (cutoff > DateTime.MinValue)
        {
            accounts = accounts.Where(a => a.LastMainCalcAtUtc == null || a.LastMainCalcAtUtc < cutoff);
        }

        return accounts
            .OrderBy(a => a.LastMainCalcAtUtc == null ? 0 : 1)
            .ThenBy(a => a.LastMainCalcAtUtc)
            .Take(Math.Max(1, batchSize))
            .Select(a => new AccountKey(a.PlatformId, a.Puuid))
            .ToListAsync(ct);
    }

    public Task<List<AccountKey>> GetAccountsForActivityCheckAsync(DateTime cutoff, int batchSize, CancellationToken ct)
    {
        // Deliberately NOT filtered on IsActive: an already-deactivated main is
        // excluded from match ingestion and from main analysis, so this mastery
        // check is its only way back (#900). Dropping it here would make
        // deactivation permanent.
        var accounts = db.RiotAccounts
            .AsNoTracking()
            .Where(account => account.Status == RiotAccountStatus.Active
                              && db.MainChampionStats.Any(stat =>
                                  stat.PlatformId == account.PlatformId
                                  && stat.Puuid == account.Puuid
                                  && stat.IsMain));

        if (cutoff > DateTime.MinValue)
        {
            accounts = accounts.Where(a => a.LastActivityCheckAtUtc == null || a.LastActivityCheckAtUtc < cutoff);
        }

        return accounts
            .OrderBy(a => a.LastActivityCheckAtUtc == null ? 0 : 1)
            .ThenBy(a => a.LastActivityCheckAtUtc)
            .Take(Math.Max(1, batchSize))
            .Select(a => new AccountKey(a.PlatformId, a.Puuid))
            .ToListAsync(ct);
    }

    public async Task<List<AccountKey>> ClaimAccountsForMatchIngestAtomicallyAsync(
        IReadOnlyDictionary<string, int> platformQuotas,
        int batchSize,
        double establishedMainShare,
        DateTime nowUtc,
        TimeSpan lease,
        CancellationToken ct)
    {
        var quotas = platformQuotas
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .GroupBy(entry => entry.Key.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Math.Max(0, group.Max(entry => entry.Value)), StringComparer.Ordinal);

        if (quotas.Count == 0)
        {
            return [];
        }

        var safeBatchSize = Math.Max(1, batchSize);
        var safeLease = lease > TimeSpan.Zero ? lease : TimeSpan.FromMinutes(30);
        var leaseCutoff = nowUtc - safeLease;

        // Over-fetch per platform per class, as the single pre-#900 query did: a claim can
        // lose the race for a row, and a platform short on one class must be able to absorb
        // another platform's released quota, so both need a reserve beyond their own slice.
        // Bounded by the whole batch because that is the most any one platform can end up
        // claiming once every other platform has spilled to it.
        var perPlatformFetchCap = safeBatchSize;

        // Ordered so the batch is reproducible: the spill pass walks platforms in this order.
        var platforms = quotas.Keys.OrderBy(platform => platform, StringComparer.Ordinal).ToList();

        // Both classes are expressed as an EXISTS over the account row rather than as a
        // join on a projected key set: one row per account without a Distinct, and the
        // ordering columns stay on riot_accounts where the claim index lives.
        //
        // Inactive mains (#900) are deliberately out of the first one: an account whose
        // mastery last-play went stale returns nothing but still costs a full match-v5
        // page every cycle, which is exactly the budget we want to move to players who
        // actually play.
        var establishedByPlatform = new Dictionary<string, List<AccountKey>>(StringComparer.Ordinal);
        var queuedByPlatform = new Dictionary<string, List<AccountKey>>(StringComparer.Ordinal);

        foreach (var platform in platforms)
        {
            establishedByPlatform[platform] = await SelectClaimableAsync(
                account => db.MainChampionStats.Any(stat =>
                    stat.IsMain
                    && stat.IsActive
                    && stat.PlatformId == account.PlatformId
                    && stat.Puuid == account.Puuid),
                platform,
                leaseCutoff,
                perPlatformFetchCap,
                ct);

            queuedByPlatform[platform] = await SelectClaimableAsync(
                account => db.MainCandidates.Any(candidate =>
                    candidate.Status == MainCandidateStatus.Queued
                    && candidate.PlatformId == account.PlatformId
                    && candidate.Puuid == account.Puuid),
                platform,
                leaseCutoff,
                perPlatformFetchCap,
                ct);
        }

        var ordered = new List<AccountKey>(safeBatchSize * 2);
        var seen = new HashSet<AccountKey>();
        var cursors = platforms.ToDictionary(
            platform => platform,
            _ => new ClassCursor(),
            StringComparer.Ordinal);

        // Pass 1 — each platform fills its own quota, applying the established/queued share
        // inside it. Depth over breadth (#900) is a per-platform rule: most of a platform's
        // slots go to re-ingesting the mains we already track there, the rest to its new
        // candidates, and whichever class that platform is short on spills to the other
        // without leaving the platform.
        foreach (var platform in platforms)
        {
            var quota = Math.Min(quotas[platform], safeBatchSize);
            var establishedQuota = (int)Math.Ceiling(quota * Math.Clamp(establishedMainShare, 0, 1));
            var taken = 0;

            taken += Append(platform, established: true, Math.Min(establishedQuota, quota - taken));
            taken += Append(platform, established: false, quota - taken);
            Append(platform, established: true, quota - taken);
        }

        // Pass 2 — spill. Quotas are floors, not partitions (#1150): a platform with fewer
        // claimable accounts than its share must not idle the batch. The spill is round-robin
        // rather than "next platform's whole reserve" on purpose — handing the entire unused
        // remainder to whichever platform happens to sort first is how a cross-platform
        // ordering behaved in the first place, and it would quietly restore the imbalance the
        // quotas exist to correct.
        var progressed = true;
        while (ordered.Count < safeBatchSize && progressed)
        {
            progressed = false;
            foreach (var platform in platforms)
            {
                if (ordered.Count >= safeBatchSize)
                {
                    break;
                }

                // Established first within a platform, same priority as its own quota pass.
                if (Append(platform, established: true, 1) > 0 || Append(platform, established: false, 1) > 0)
                {
                    progressed = true;
                }
            }
        }

        // Pass 3 — the race reserve, beyond the batch size. Every candidate here is only
        // claimed if an earlier one lost its ExecuteUpdate race, so this is spare capacity,
        // not extra work. Round-robin again for the same reason as the spill.
        progressed = true;
        while (progressed)
        {
            progressed = false;
            foreach (var platform in platforms)
            {
                if (Append(platform, established: true, 1) > 0 || Append(platform, established: false, 1) > 0)
                {
                    progressed = true;
                }
            }
        }

        var claimed = new List<AccountKey>();
        foreach (var candidate in ordered)
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

        // Appends up to `count` not-yet-taken keys of one class on one platform, advancing
        // that class's cursor so every pass resumes where the previous one stopped instead of
        // rescanning from the top. Returns how many it actually appended.
        int Append(string platform, bool established, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            var source = established ? establishedByPlatform[platform] : queuedByPlatform[platform];
            var cursor = cursors[platform];
            var index = established ? cursor.Established : cursor.Queued;
            var appended = 0;

            while (index < source.Count && appended < count)
            {
                var key = source[index];
                index++;

                // An account can be both an established main and a queued candidate; it must
                // be claimed once and count against one class only.
                if (!seen.Add(key))
                {
                    continue;
                }

                ordered.Add(key);
                appended++;
            }

            if (established)
            {
                cursor.Established = index;
            }
            else
            {
                cursor.Queued = index;
            }

            return appended;
        }
    }

    /// <summary>Per-platform read positions into the two claimable class lists.</summary>
    private sealed class ClassCursor
    {
        public int Established { get; set; }
        public int Queued { get; set; }
    }

    /// <summary>
    /// The accounts on one platform matching <paramref name="membership"/> that are currently
    /// claimable for match ingestion (active account, Idle or an expired Processing lease),
    /// oldest-ingested first.
    /// <para>
    /// Scoped to a single platform since #1150. Nulls-first — never-ingested accounts before
    /// everything else — is the right priority <em>within</em> a platform, but it was the
    /// mechanism of the imbalance across platforms: the region creating the most new accounts
    /// automatically captured the most of the batch, which is the region that had just been
    /// ingested most.
    /// </para>
    /// </summary>
    private Task<List<AccountKey>> SelectClaimableAsync(
        Expression<Func<RiotAccount, bool>> membership,
        string normalizedPlatform,
        DateTime leaseCutoff,
        int take,
        CancellationToken ct)
        => db.RiotAccounts
            .AsNoTracking()
            .Where(account => account.Status == RiotAccountStatus.Active
                              && account.PlatformId == normalizedPlatform
                              && (account.MatchIngestStatus == MatchIngestStatus.Idle
                                  || (account.MatchIngestStatus == MatchIngestStatus.Processing
                                      && account.MatchIngestClaimedAtUtc != null
                                      && account.MatchIngestClaimedAtUtc < leaseCutoff)))
            .Where(membership)
            .OrderBy(account => account.LastMatchIngestAtUtc == null ? 0 : 1)
            .ThenBy(account => account.LastMatchIngestAtUtc)
            .Take(take)
            .Select(account => new AccountKey(account.PlatformId, account.Puuid))
            .ToListAsync(ct);

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
