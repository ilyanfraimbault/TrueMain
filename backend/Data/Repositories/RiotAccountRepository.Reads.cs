using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

/// <summary>
/// Account lookups by key, and the batch selections the refresh and activity-check processes work
/// from.
/// </summary>
public sealed partial class RiotAccountRepository
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

    public async Task<HashSet<string>> GetProfileFreshPuuidsAsync(
        IReadOnlyCollection<string> puuids,
        DateTime freshSinceUtc,
        CancellationToken ct)
    {
        if (puuids.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var puuidArray = puuids.Distinct(StringComparer.Ordinal).ToArray();
        var found = await db.RiotAccounts
            .AsNoTracking()
            .Where(a => puuidArray.Contains(a.Puuid)
                        && a.LastProfileSyncAtUtc != null
                        && a.LastProfileSyncAtUtc >= freshSinceUtc)
            .Select(a => a.Puuid)
            .ToListAsync(ct);

        return found.ToHashSet(StringComparer.Ordinal);
    }

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

    public Task<List<AccountKey>> GetAccountsForActivityCheckAsync(
        DateTime cutoff, DateTime observedSince, int batchSize, CancellationToken ct)
    {
        // Not on IsActive: the way back for a deactivated main (#900). Seen in a match and read since observedSince: skip (#1475).
        var accounts = db.RiotAccounts.AsNoTracking()
            .Where(account => account.Status == RiotAccountStatus.Active
                              && db.MainChampionStats.Any(stat =>
                                  stat.PlatformId == account.PlatformId && stat.Puuid == account.Puuid && stat.IsMain)
                              && (account.LastSeenInMatchAtUtc == null || account.LastSeenInMatchAtUtc < observedSince
                                  || account.LastActivityCheckAtUtc == null || account.LastActivityCheckAtUtc < observedSince));

        if (cutoff > DateTime.MinValue)
        {
            accounts = accounts.Where(a => a.LastActivityCheckAtUtc == null || a.LastActivityCheckAtUtc < cutoff);
        }

        // Within the due set, a main never mastery-read goes first: the truemain score needs it (#1701).
        return accounts
            .OrderBy(a => a.LastActivityCheckAtUtc == null ? 0 : 1)
            .ThenBy(a => db.MainChampionStats.Any(s =>
                s.PlatformId == a.PlatformId && s.Puuid == a.Puuid && s.IsMain && s.MasteryPoints == null) ? 0 : 1)
            .ThenBy(a => a.LastActivityCheckAtUtc)
            .Take(Math.Max(1, batchSize))
            .Select(a => new AccountKey(a.PlatformId, a.Puuid))
            .ToListAsync(ct);
    }
}
