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
/// runs. A row whose account has <em>no</em> snapshot yet is left unenriched
/// (empty) rather than stamped <see cref="EloBracket.Unranked"/>, so a later
/// cycle re-evaluates it once the first snapshot arrives — the row then self-heals
/// to its real band, exactly like the champion aggregation, which re-derives the
/// band from the current snapshots every cycle. Stamping UNRANKED up front would
/// be permanent and diverge from builds/tierlist for the same game. Once a
/// snapshot exists the row is written its final band (a real tier or genuine
/// <c>UNRANKED</c>) and never revisited, so steady-state runs only touch
/// newly-ingested or still-snapshotless rows.
/// </summary>
public sealed class MatchParticipantEloBracketEnrichmentProcess(
    ILogger<MatchParticipantEloBracketEnrichmentProcess> logger,
    IDbContextFactory<TrueMainDbContext> dbContextFactory) : IIngestorProcess
{
    // Bounds the working set per batch: only the (id, account, game-start) tuple
    // and the involved accounts' snapshots are materialised, never the wide
    // participant rows. The scan is paged by a keyset cursor on Id, so the loop
    // advances even through rows it deliberately leaves unenriched (accounts with
    // no snapshot yet) and terminates at the end of the set.
    private const int BatchSize = 5000;

    public string Name => "MatchParticipantEloBracketEnrichment";

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        var totalStamped = 0;
        var totalDeferred = 0;
        var batches = 0;
        Guid? afterId = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);

            // Unenriched tracked participants + their game start (every participant
            // has a match via FK, so an inner join loses none), paged by a keyset
            // cursor on Id. The cursor is essential now that a batch may leave some
            // rows unenriched (accounts with no snapshot yet): without it a page
            // full of such rows would be re-selected forever. No queue filter:
            // stamping every tracked row — queue or not — drains the unenriched set
            // and is harmless for the reads, which only touch the configured queue.
            var pending = db.MatchParticipants
                .AsNoTracking()
                .Where(p => p.RiotAccountId != null && p.EloBracket == string.Empty);
            if (afterId is { } cursor)
            {
                pending = pending.Where(p => p.Id > cursor);
            }

            var batch = await pending
                .Join(
                    db.Matches.AsNoTracking(),
                    p => p.MatchId,
                    m => m.Id,
                    (p, m) => new { p.Id, AccountId = p.RiotAccountId!.Value, m.GameStartTimeUtc })
                .OrderBy(row => row.Id)
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
            // per row. A row whose account has no snapshot yet is deferred (left
            // empty) rather than stamped UNRANKED, so a later cycle re-evaluates it
            // once AccountRefresh captures the first snapshot — it then self-heals
            // to the real band, matching the aggregation. Once a snapshot exists the
            // resolved band (a real tier or genuine UNRANKED) is final.
            var idsByBand = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
            var deferred = 0;
            foreach (var row in batch)
            {
                if (!snapshotsByAccount.TryGetValue(row.AccountId, out var accountSnapshots))
                {
                    deferred++;
                    continue;
                }

                var band = EloBracketResolver.FromNearestSnapshot(accountSnapshots, row.GameStartTimeUtc);
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

            totalStamped += batch.Count - deferred;
            totalDeferred += deferred;
            batches++;
            afterId = batch[^1].Id;

            // A short page means we reached the end of the unenriched set for this
            // cycle — avoid a final empty round-trip.
            if (batch.Count < BatchSize)
            {
                break;
            }
        }

        logger.LogInformation(
            "Match participant elo-bracket enrichment: stamped={Stamped} rows, deferred={Deferred} " +
            "(account not yet rank-synced) across {Batches} batch(es).",
            totalStamped, totalDeferred, batches);

        return new { stamped = totalStamped, deferred = totalDeferred, batches };
    }
}
