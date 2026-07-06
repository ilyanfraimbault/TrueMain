using Core.Lol.Ranking;
using Data;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes;

/// <summary>
/// Stamps <c>match_participants.elo_bracket</c> for tracked rows (those with a
/// <c>RiotAccountId</c>) that have not been enriched yet — the per-game elo band
/// derived from the account's nearest <c>rank_snapshots</c> capture to the match
/// start (<see cref="EloBracketResolver"/>). This is the enabler that lets every
/// champion-page panel (live matchups / scaling / roam / powerspikes / item
/// timings and the pre-aggregated matchup / timeline-lead tables) filter by rank,
/// mirroring the band already stored on <c>champion_aggregate_scopes</c>.
///
/// Runs before the champion aggregations in the cycle so they read a freshly
/// stamped column; it uses the snapshots captured by prior <c>AccountRefresh</c>
/// runs, so a game ingested before its account's first rank sync stays
/// <see cref="EloBracket.Unranked"/> — the same one-cycle lag the scope
/// aggregation already accepts. Stamp-once: each row is written exactly one band
/// (a real tier or <c>UNRANKED</c>), so the unenriched set drains and steady-state
/// runs only touch newly-ingested rows.
/// </summary>
public sealed class MatchParticipantEloBracketEnrichmentProcess(
    ILogger<MatchParticipantEloBracketEnrichmentProcess> logger,
    IDbContextFactory<TrueMainDbContext> dbContextFactory) : IIngestorProcess
{
    // Bounds the working set per batch: only the (id, account, game-start) tuple
    // and the involved accounts' snapshots are materialised, never the wide
    // participant rows. Each batch stamps its rows to a non-empty band, so the
    // "elo_bracket = ''" set shrinks and the loop terminates.
    private const int BatchSize = 5000;

    public string Name => "MatchParticipantEloBracketEnrichment";

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        var totalStamped = 0;
        var batches = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);

            // Unenriched tracked participants + their game start (every participant
            // has a match via FK, so an inner join loses none). No queue filter:
            // stamping every tracked row — queue or not — drains the unenriched set
            // and is harmless for the reads, which only touch the configured queue.
            var batch = await db.MatchParticipants
                .AsNoTracking()
                .Where(p => p.RiotAccountId != null && p.EloBracket == string.Empty)
                .Join(
                    db.Matches.AsNoTracking(),
                    p => p.MatchId,
                    m => m.Id,
                    (p, m) => new { p.Id, AccountId = p.RiotAccountId!.Value, m.GameStartTimeUtc })
                .Take(BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0)
            {
                break;
            }

            var accountIds = batch.Select(row => row.AccountId).Distinct().ToList();
            var snapshots = await db.RankSnapshots
                .AsNoTracking()
                .Where(snapshot => accountIds.Contains(snapshot.RiotAccountId))
                .Select(snapshot => new { snapshot.RiotAccountId, snapshot.CapturedAtUtc, snapshot.Tier })
                .ToListAsync(ct);

            var snapshotsByAccount = snapshots
                .GroupBy(snapshot => snapshot.RiotAccountId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyCollection<(DateTime, string?)>)group
                        .Select(snapshot => (snapshot.CapturedAtUtc, (string?)snapshot.Tier))
                        .ToList());

            // Resolve each row's band, then group ids by band so the writes are a
            // handful of set-based UPDATEs (one per distinct band) instead of one
            // per row. Accounts with no snapshot resolve to UNRANKED.
            var idsByBand = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
            foreach (var row in batch)
            {
                var band = snapshotsByAccount.TryGetValue(row.AccountId, out var accountSnapshots)
                    ? EloBracketResolver.FromNearestSnapshot(accountSnapshots, row.GameStartTimeUtc)
                    : EloBracket.Unranked;

                if (!idsByBand.TryGetValue(band, out var ids))
                {
                    ids = [];
                    idsByBand[band] = ids;
                }
                ids.Add(row.Id);
            }

            foreach (var (band, ids) in idsByBand)
            {
                await db.MatchParticipants
                    .Where(p => ids.Contains(p.Id))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.EloBracket, band), ct);
            }

            totalStamped += batch.Count;
            batches++;

            // A short batch means the source query returned fewer than a full page,
            // so there is nothing left to stamp — avoid a final empty round-trip.
            if (batch.Count < BatchSize)
            {
                break;
            }
        }

        logger.LogInformation(
            "Match participant elo-bracket enrichment: stamped={Stamped} rows across {Batches} batch(es).",
            totalStamped, batches);

        return new { stamped = totalStamped, batches };
    }
}
