using System.Linq.Expressions;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

/// <summary>
/// The account store of the ingest side. This file holds the match-ingest claim lifecycle (claiming a
/// per-platform batch under a lease, releasing expired leases, recording a visit); the lookups and the
/// refresh / activity-check selections live in <c>RiotAccountRepository.Reads.cs</c>.
/// </summary>
public sealed partial class RiotAccountRepository(TrueMainDbContext db) : IRiotAccountRepository
{
    public async Task<List<AccountKey>> ClaimAccountsForMatchIngestAtomicallyAsync(
        IReadOnlyDictionary<string, int> platformQuotas,
        int batchSize,
        IReadOnlyDictionary<string, double> establishedMainShares,
        DateTime nowUtc,
        TimeSpan lease,
        CancellationToken ct)
    {
        var quotas = PlatformKeyed.Normalize(platformQuotas, values => Math.Max(0, values.Max()));
        var shares = PlatformKeyed.Normalize(establishedMainShares, values => Math.Clamp(values.Max(), 0d, 1d));

        if (quotas.Count == 0)
        {
            return [];
        }

        var safeBatchSize = Math.Max(1, batchSize);
        var safeLease = lease > TimeSpan.Zero ? lease : TimeSpan.FromMinutes(30);
        var leaseCutoff = nowUtc - safeLease;

        // Ordered so the batch is reproducible: the spill pass walks platforms in this order.
        var platforms = quotas.Keys.OrderBy(platform => platform, StringComparer.Ordinal).ToList();

        // Over-fetch per platform per class, keeping the 4x race reserve the single pre-#1150
        // query had: a claim can lose the ExecuteUpdate race for a row, and without spare rows
        // behind the quota the batch silently comes back short. Scaling on the platform's own
        // quota rather than on the batch keeps that reserve proportional however the budget is
        // split, and the batch-sized floor is what lets one platform absorb every other
        // platform's released quota when it is the only one with work.
        int FetchCapFor(string platform) => Math.Max(safeBatchSize, quotas[platform] * 4);

        // Both classes are expressed as an EXISTS over the account row rather than as a
        // join on a projected key set: one row per account without a Distinct, and the
        // ordering columns stay on riot_accounts where the claim index lives.
        //
        // Both live in MatchIngestClaimClasses, which says why each excludes what it does.
        var establishedByPlatform = new Dictionary<string, List<AccountKey>>(StringComparer.Ordinal);
        var queuedByPlatform = new Dictionary<string, List<AccountKey>>(StringComparer.Ordinal);

        foreach (var platform in platforms)
        {
            establishedByPlatform[platform] = await SelectClaimableAsync(
                MatchIngestClaimClasses.EstablishedMain(db),
                platform,
                leaseCutoff,
                FetchCapFor(platform),
                ct);

            // Breadth is for accounts never ingested (#1535): an ingested one is depth's.
            queuedByPlatform[platform] = await SelectClaimableAsync(
                MatchIngestClaimClasses.NewCandidate(db),
                platform,
                leaseCutoff,
                FetchCapFor(platform),
                ct);
        }

        var ordered = new List<AccountKey>(safeBatchSize * 2);
        var seen = new HashSet<AccountKey>();
        var cursors = platforms.ToDictionary(
            platform => platform,
            _ => new ClassCursor(),
            StringComparer.Ordinal);

        // Pass 1 — each platform fills its own quota, applying its own established/queued share
        // inside it (#1533). Depth over breadth (#900) is a per-platform rule, and so is the
        // share: a saturated region spends on depth while a thin one spends on breadth. Whichever
        // class that platform is short on spills to the other without leaving the platform.
        foreach (var platform in platforms)
        {
            var quota = Math.Min(quotas[platform], safeBatchSize);
            var establishedQuota = (int)Math.Ceiling(quota * shares.GetValueOrDefault(platform));
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
            // Never-ingested accounts first, then by how many ranked games the player has
            // actually played since the last visit, then oldest-visited (#1360).
            //
            // Ordering by age alone spent the batch on whoever had waited longest, whether or
            // not they had played: production revisited an account every 27 days (median) with
            // a 20-game window, so the most active mains — the ones the site is about — were
            // losing games between visits while idle accounts consumed slots. The ladder sweep
            // already refreshes wins/losses for every tracked Emerald+ account at ~310 calls a
            // run, so this signal is free.
            //
            // An account with no ladder reading has an owed of zero and falls back to the
            // age ordering, which is exactly the behaviour it had before — no starvation is
            // introduced, the accounts that played simply stop queueing behind those that
            // did not.
            .OrderBy(account => account.LastMatchIngestAtUtc == null ? 0 : 1)
            // This restates LadderGamesOwed.From as an expression tree, because a query cannot
            // call it. The two must stay in step; that type documents why each case yields
            // zero — a missing baseline is "unknown", not "the whole season", and a season
            // reset makes the raw difference negative for every account at once.
            .ThenByDescending(account =>
                account.LadderGames != null && account.LadderGamesAtLastIngest != null
                    ? Math.Max(0, account.LadderGames.Value - account.LadderGamesAtLastIngest.Value)
                    : 0)
            .ThenBy(account => account.LastMatchIngestAtUtc)
            .Take(take)
            .Select(account => new AccountKey(account.PlatformId, account.Puuid))
            .ToListAsync(ct);

    public Task<int> ReleaseExpiredMatchIngestClaimsAsync(DateTime leaseCutoffUtc, CancellationToken ct)
        => db.RiotAccounts
            // Served by IX_riot_accounts_ingest_claim_lease, the partial index this state
            // already has: (MatchIngestStatus, MatchIngestClaimedAtUtc, ...) WHERE status <> 0.
            .Where(account => account.MatchIngestStatus == MatchIngestStatus.Processing
                              && (account.MatchIngestClaimedAtUtc == null
                                  || account.MatchIngestClaimedAtUtc < leaseCutoffUtc))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(account => account.MatchIngestStatus, MatchIngestStatus.Idle)
                    .SetProperty(account => account.MatchIngestClaimedAtUtc, (DateTime?)null),
                ct);

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
                setters => setters
                    .SetProperty(a => a.LastMatchIngestAtUtc, atUtc)
                    // Reset the games-owed baseline in the same statement that records the
                    // visit (#1360). Doing it anywhere else would leave a window in which the
                    // account reads as freshly ingested but still owing every game it owed
                    // before, and the claim would hand it straight back at the top of the
                    // next batch.
                    .SetProperty(a => a.LadderGamesAtLastIngest, a => a.LadderGames),
                ct);

    public void Add(RiotAccount account)
        => db.RiotAccounts.Add(account);
}
