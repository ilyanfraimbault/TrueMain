using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed partial class RiotAccountRepository
{
    public Task<List<AccountKey>> GetAccountsForMainAnalysisAsync(DateTime cutoff, int batchSize, CancellationToken ct)
    {
        // Three eligibility paths, OR'd so each account appears once:
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
        //  3. An account already ingested once and seen in a game played since its
        //     last main calc (#2039). A demoted or never-main account matches
        //     neither path above, no claim class revisits it (depth is for active
        //     mains, breadth for never-ingested accounts, #1535), so without this
        //     it froze on its last verdict while the harvest kept storing its games.
        //     Gated on a new game, not on the cutoff alone, so a non-main is only
        //     re-read when there is something new to read.
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
                          && stat.IsActive)
                      || (account.LastMatchIngestAtUtc != null
                          && account.LastSeenInMatchAtUtc != null
                          && (account.LastMainCalcAtUtc == null
                              || account.LastSeenInMatchAtUtc > account.LastMainCalcAtUtc)))
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
}
