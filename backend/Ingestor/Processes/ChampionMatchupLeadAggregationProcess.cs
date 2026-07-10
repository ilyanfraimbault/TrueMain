using Core.Lol.Patches;
using Core.Options;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

/// <summary>
/// Pre-aggregates the champion page's two heaviest read slices into
/// <c>champion_matchup_stats</c> and <c>champion_timeline_lead_stats</c> (#606).
/// Both were live self-joins over the multi-GB <c>match_participants</c> /
/// <c>match_participant_timeline_snapshots</c> tables, single-threaded since
/// parallel query is disabled (#589) — so they dominated champion-page latency.
///
/// Work is chunked per champion: each champion's matchup counts and timeline diff
/// totals are computed by a GROUP BY pushed entirely to Postgres (only the small
/// aggregated rows cross the wire, never the raw rows — so no OOM, unlike the
/// pattern aggregation #600), then written under a per-champion transaction with
/// freeze-safe replace-by-scope. Rows are stored WITHOUT the games floor: the read
/// side folds them to the requested patch scope and applies the floor on the
/// merged total, so the all-patches view floors on the real total.
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

    // Snapshots are sampled every minute since #567, but games ingested before
    // that only have these canonical marks. Pin the aggregate to them so the curve
    // is identical across cohorts (mirrors the former live read).
    private static readonly int[] LeadIntervalMinutes = [5, 10, 15, 20, 30];

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
            championIds = await LoadChampionIdsAsync(db, ct);
        }

        if (livePatches.Count == 0)
        {
            logger.LogInformation("No live patches available for champion matchup/lead aggregation.");
            return new { reason = "No live patches available.", champions = 0, matchupRows = 0, leadRows = 0 };
        }

        // EF translates List.Contains to `= ANY (...)`; HashSet does not. Keep both
        // shapes: the list for the SQL delete, the set for the in-memory merge.
        var livePatchList = livePatches.ToList();

        var processed = 0;
        var matchupRowCount = 0;
        var leadRowCount = 0;

        foreach (var championId in championIds)
        {
            ct.ThrowIfCancellationRequested();

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);

            var matchupRows = await ComputeMatchupRowsAsync(db, championId, queueId, livePatches, aggregatedAtUtc, ct);
            var leadRows = await ComputeLeadRowsAsync(db, championId, queueId, livePatches, aggregatedAtUtc, ct);

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
            await db.ChampionTimelineLeadStats
                .Where(s => s.ChampionId == championId && livePatchList.Contains(s.Patch))
                .ExecuteDeleteAsync(ct);

            if (matchupRows.Count > 0)
            {
                db.ChampionMatchupStats.AddRange(matchupRows);
            }

            if (leadRows.Count > 0)
            {
                db.ChampionTimelineLeadStats.AddRange(leadRows);
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            processed++;
            matchupRowCount += matchupRows.Count;
            leadRowCount += leadRows.Count;
        }

        logger.LogInformation(
            "Champion matchup/lead aggregation summary: champions={Champions}, matchupRows={MatchupRows}, leadRows={LeadRows}, livePatches={LivePatches}.",
            processed,
            matchupRowCount,
            leadRowCount,
            livePatchList.Count);

        return new
        {
            champions = processed,
            matchupRows = matchupRowCount,
            leadRows = leadRowCount,
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
        CancellationToken ct)
    {
        // Champions with at least one "main" account — a superset of the champions
        // that can produce source rows. The per-champion matchup/lead queries
        // re-apply the full tracked-account/queue filter, so a champion with no
        // qualifying rows just yields empty results. Derived from
        // main_champion_stats (small: one row per tracked account/champion)
        // rather than a DISTINCT over match_participants, which scanned the whole
        // 35 GB table and blew the command timeout now that parallel query is
        // disabled (max_parallel_workers_per_gather=0, #589) — the same failure
        // mode already fixed for pattern aggregation in #604.
        var tracked = await db.MainChampionStats
            .AsNoTracking()
            .Where(stat => stat.IsMain)
            .Select(stat => stat.ChampionId)
            .Distinct()
            .ToListAsync(ct);

        var existingMatchup = await db.ChampionMatchupStats
            .AsNoTracking()
            .Select(s => s.ChampionId)
            .Distinct()
            .ToListAsync(ct);

        var existingLead = await db.ChampionTimelineLeadStats
            .AsNoTracking()
            .Select(s => s.ChampionId)
            .Distinct()
            .ToListAsync(ct);

        return tracked
            .Union(existingMatchup)
            .Union(existingLead)
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
                (x, p2) => new { x.P1.TeamPosition, Opponent = p2.ChampionId, x.GameVersion, x.P1.EloBracket, x.P1.Win })
            .GroupBy(x => new { x.TeamPosition, x.Opponent, x.GameVersion, x.EloBracket })
            .Select(g => new
            {
                g.Key.TeamPosition,
                g.Key.Opponent,
                g.Key.GameVersion,
                g.Key.EloBracket,
                Games = g.Count(),
                Wins = g.Sum(x => x.Win ? 1 : 0),
            })
            .ToListAsync(ct);

        // Fold the raw Riot versions (hotfix builds) down to the canonical patch.
        // The elo band is carried through so one row per (position, opponent,
        // patch, band) is stored — the read seeks the bands it wants.
        return raw
            .GroupBy(r => new { r.TeamPosition, r.Opponent, Patch = PatchVersion.Normalize(r.GameVersion), r.EloBracket })
            .Where(g => livePatches.Contains(g.Key.Patch))
            .Select(g => new ChampionMatchupStat
            {
                ChampionId = championId,
                TeamPosition = g.Key.TeamPosition,
                OpponentChampionId = g.Key.Opponent,
                Patch = g.Key.Patch,
                EloBracket = g.Key.EloBracket,
                Games = g.Sum(x => x.Games),
                Wins = g.Sum(x => x.Wins),
                AggregatedAtUtc = aggregatedAtUtc,
            })
            .ToList();
    }

    private static async Task<List<ChampionTimelineLeadStat>> ComputeLeadRowsAsync(
        TrueMainDbContext db,
        int championId,
        int queueId,
        HashSet<string> livePatches,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        // Pair the champion with its lane opponent, join both sides' per-interval
        // snapshots on the same minute mark, and sum the per-game diffs + count
        // games per (position, raw GameVersion, interval). The sargable IN on the
        // opponent snapshot mirrors the live read's #594 fix (prunes the opponent
        // side to the five marks up front instead of a full single-threaded scan).
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
                (x, p2) => new
                {
                    x.GameVersion,
                    Position = x.P1.TeamPosition,
                    x.P1.EloBracket,
                    P1MatchId = x.P1.MatchId,
                    P1ParticipantId = x.P1.ParticipantId,
                    P2MatchId = p2.MatchId,
                    P2ParticipantId = p2.ParticipantId,
                })
            .SelectMany(
                x => db.MatchParticipantTimelineSnapshots.Where(s1 =>
                    s1.MatchId == x.P1MatchId
                    && s1.ParticipantId == x.P1ParticipantId
                    && LeadIntervalMinutes.Contains(s1.IntervalMinute)),
                (x, s1) => new { x.GameVersion, x.Position, x.EloBracket, x.P2MatchId, x.P2ParticipantId, S1 = s1 })
            .SelectMany(
                x => db.MatchParticipantTimelineSnapshots.Where(s2 =>
                    s2.MatchId == x.P2MatchId
                    && s2.ParticipantId == x.P2ParticipantId
                    && LeadIntervalMinutes.Contains(s2.IntervalMinute)
                    && s2.IntervalMinute == x.S1.IntervalMinute),
                (x, s2) => new
                {
                    x.GameVersion,
                    x.Position,
                    x.EloBracket,
                    x.S1.IntervalMinute,
                    GoldDiff = x.S1.TotalGold - s2.TotalGold,
                    CsDiff = x.S1.MinionsKilled + x.S1.JungleMinionsKilled - s2.MinionsKilled - s2.JungleMinionsKilled,
                    KillsDiff = x.S1.Kills - s2.Kills,
                    LevelDiff = x.S1.Level - s2.Level,
                    XpDiff = x.S1.Xp - s2.Xp,
                    DamageDiff = x.S1.DamageToChampions - s2.DamageToChampions,
                })
            .GroupBy(x => new { x.GameVersion, x.Position, x.EloBracket, x.IntervalMinute })
            .Select(g => new
            {
                g.Key.GameVersion,
                g.Key.Position,
                g.Key.EloBracket,
                g.Key.IntervalMinute,
                Games = g.Count(),
                GoldDiff = g.Sum(x => (long)x.GoldDiff),
                CsDiff = g.Sum(x => (long)x.CsDiff),
                KillsDiff = g.Sum(x => (long)x.KillsDiff),
                LevelDiff = g.Sum(x => (long)x.LevelDiff),
                XpDiff = g.Sum(x => (long)x.XpDiff),
                DamageDiff = g.Sum(x => (long)x.DamageDiff),
            })
            .ToListAsync(ct);

        return raw
            .GroupBy(r => new { r.Position, Patch = PatchVersion.Normalize(r.GameVersion), r.EloBracket, r.IntervalMinute })
            .Where(g => livePatches.Contains(g.Key.Patch))
            .Select(g => new ChampionTimelineLeadStat
            {
                ChampionId = championId,
                TeamPosition = g.Key.Position,
                Patch = g.Key.Patch,
                EloBracket = g.Key.EloBracket,
                IntervalMinute = g.Key.IntervalMinute,
                Games = g.Sum(x => x.Games),
                TotalGoldDiff = g.Sum(x => x.GoldDiff),
                TotalCsDiff = g.Sum(x => x.CsDiff),
                TotalKillsDiff = g.Sum(x => x.KillsDiff),
                TotalLevelDiff = g.Sum(x => x.LevelDiff),
                TotalXpDiff = g.Sum(x => x.XpDiff),
                TotalDamageDiff = g.Sum(x => x.DamageDiff),
                AggregatedAtUtc = aggregatedAtUtc,
            })
            .ToList();
    }
}
