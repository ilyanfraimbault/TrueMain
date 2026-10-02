using Core.Lol.Patches;
using Core.Options;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

/// <summary>
/// Pre-aggregates the champion page's global matchups leaderboard into
/// <c>champion_matchup_stats</c> (#606). It was a live self-join over the multi-GB
/// <c>match_participants</c> table, single-threaded since parallel query is
/// disabled (#589) — so it dominated champion-page latency.
///
/// Work is chunked per champion: each champion's matchup counts are computed by a
/// GROUP BY pushed entirely to Postgres (only the small aggregated rows cross the
/// wire, never the raw rows — so no OOM, unlike the pattern aggregation #600), then
/// written under a per-champion transaction with freeze-safe replace-by-scope. Rows
/// are stored WITHOUT the games floor: the read side folds them to the requested
/// patch scope and applies the floor on the merged total, so the all-patches view
/// floors on the real total.
///
/// The sibling <c>champion_timeline_lead_stats</c> is no longer produced here: it
/// (and the powerspike aggregates) are folded incrementally at timeline ingestion,
/// so the raw per-minute snapshot grid could be dropped entirely.
/// </summary>
public sealed class ChampionMatchupLeadAggregationProcess(
    ILogger<ChampionMatchupLeadAggregationProcess> logger,
    IOptions<MainAnalysisOptions> analysisOptions,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    TimeProvider timeProvider) : IIngestorProcess
{
    // The five canonical lane positions. Off-position rows (empty/garbage
    // TeamPosition) can never be a real lane matchup, so they are excluded up
    // front rather than stored as junk the reads would never ask for.
    private static readonly string[] CanonicalPositions = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

    public string Name => "ChampionMatchupLeadAggregation";

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        var queueId = (int)analysisOptions.Value.QueueId;
        var aggregatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        HashSet<string> livePatches;
        List<int> championIds;
        await using (var db = await dbContextFactory.CreateDbContextAsync(ct))
        {
            livePatches = await LoadLivePatchesAsync(db, queueId, ct);
            championIds = await LoadChampionIdsAsync(db, queueId, ct);
        }

        if (livePatches.Count == 0)
        {
            logger.LogInformation("No live patches available for champion matchup aggregation.");
            return new { reason = "No live patches available.", champions = 0, matchupRows = 0 };
        }

        // EF translates List.Contains to `= ANY (...)`; HashSet does not. Keep both
        // shapes: the list for the SQL delete, the set for the in-memory merge.
        var livePatchList = livePatches.ToList();

        var processed = 0;
        var matchupRowCount = 0;

        foreach (var championId in championIds)
        {
            ct.ThrowIfCancellationRequested();

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);

            var matchupRows = await ComputeMatchupRowsAsync(db, championId, queueId, livePatches, aggregatedAtUtc, ct);

            // Freeze-safe replace-by-scope: delete only this champion's LIVE-patch
            // rows, then insert the freshly computed ones. Patches whose match data
            // has aged out of `matches` (retention) are absent from livePatchList,
            // so their rows are never deleted — their aggregates stay frozen. Each
            // champion commits independently, so a mid-run crash leaves processed
            // champions fresh and the rest on their previous data.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            await db.ChampionMatchupStats
                .Where(s => s.ChampionId == championId && livePatchList.Contains(s.Patch))
                .ExecuteDeleteAsync(ct);

            if (matchupRows.Count > 0)
            {
                db.ChampionMatchupStats.AddRange(matchupRows);
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            processed++;
            matchupRowCount += matchupRows.Count;
        }

        logger.LogInformation(
            "Champion matchup aggregation summary: champions={Champions}, matchupRows={MatchupRows}, livePatches={LivePatches}.",
            processed,
            matchupRowCount,
            livePatchList.Count);

        return new
        {
            champions = processed,
            matchupRows = matchupRowCount,
            livePatches = livePatchList.Count
        };
    }

    private static async Task<HashSet<string>> LoadLivePatchesAsync(
        TrueMainDbContext db,
        int queueId,
        CancellationToken ct)
    {
        // matches.GameVersion is the raw Riot version (e.g. "16.5.2"); the
        // aggregate stores the normalised patch ("16.5"). Normalisation is C#
        // (EF can't translate it), so materialise the distinct raw versions —
        // retention keeps `matches` to a handful of patches — then fold in memory.
        var rawVersions = await db.Matches
            .AsNoTracking()
            .Where(match => match.QueueId == queueId)
            .Select(match => match.GameVersion)
            .Distinct()
            .ToListAsync(ct);

        return rawVersions
            .Select(PatchVersion.Normalize)
            .Where(patch => !string.IsNullOrEmpty(patch))
            .ToHashSet();
    }

    private static async Task<List<int>> LoadChampionIdsAsync(
        TrueMainDbContext db,
        int queueId,
        CancellationToken ct)
    {
        // Champions with tracked games on the queue (the ones with data to
        // aggregate) unioned with champions that already have aggregate rows (so a
        // champion that has since dropped out gets its live-patch rows pruned).
        var tracked = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => p.RiotAccountId != null && CanonicalPositions.Contains(p.TeamPosition))
            .Where(p => db.Matches.Any(m => m.Id == p.MatchId && m.QueueId == queueId))
            .Select(p => p.ChampionId)
            .Distinct()
            .ToListAsync(ct);

        var existingMatchup = await db.ChampionMatchupStats
            .AsNoTracking()
            .Select(s => s.ChampionId)
            .Distinct()
            .ToListAsync(ct);

        return tracked
            .Union(existingMatchup)
            .OrderBy(championId => championId)
            .ToList();
    }

    private static async Task<List<ChampionMatchupStat>> ComputeMatchupRowsAsync(
        TrueMainDbContext db,
        int championId,
        int queueId,
        HashSet<string> livePatches,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        // Self-join the champion's tracked rows to each lane opponent (same match
        // + position, opposite team) and count games / wins per (position,
        // opponent, raw GameVersion). Postgres does the GROUP BY; only the
        // aggregated rows return.
        var raw = await db.MatchParticipants
            .AsNoTracking()
            .Where(p1 => p1.ChampionId == championId
                && p1.RiotAccountId != null
                && CanonicalPositions.Contains(p1.TeamPosition))
            .Join(
                db.Matches.AsNoTracking().Where(m => m.QueueId == queueId),
                p1 => p1.MatchId,
                m => m.Id,
                (p1, m) => new { P1 = p1, m.GameVersion })
            .SelectMany(
                x => db.MatchParticipants.Where(p2 =>
                    p2.MatchId == x.P1.MatchId
                    && p2.TeamPosition == x.P1.TeamPosition
                    && p2.TeamId != x.P1.TeamId),
                (x, p2) => new { x.P1.TeamPosition, Opponent = p2.ChampionId, x.GameVersion, x.P1.Win })
            .GroupBy(x => new { x.TeamPosition, x.Opponent, x.GameVersion })
            .Select(g => new
            {
                g.Key.TeamPosition,
                g.Key.Opponent,
                g.Key.GameVersion,
                Games = g.Count(),
                Wins = g.Sum(x => x.Win ? 1 : 0),
            })
            .ToListAsync(ct);

        // Fold the raw Riot versions (hotfix builds) down to the canonical patch.
        return raw
            .GroupBy(r => new { r.TeamPosition, r.Opponent, Patch = PatchVersion.Normalize(r.GameVersion) })
            .Where(g => livePatches.Contains(g.Key.Patch))
            .Select(g => new ChampionMatchupStat
            {
                ChampionId = championId,
                TeamPosition = g.Key.TeamPosition,
                OpponentChampionId = g.Key.Opponent,
                Patch = g.Key.Patch,
                Games = g.Sum(x => x.Games),
                Wins = g.Sum(x => x.Wins),
                AggregatedAtUtc = aggregatedAtUtc,
            })
            .ToList();
    }
}
