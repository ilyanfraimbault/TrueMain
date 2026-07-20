using Core.Lol.Patches;
using Core.Options;
using Data;
using Data.Entities;
using Ingestor.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ingestor.Processes;

/// <summary>
/// Incrementally pre-aggregates the champion page's two heaviest read slices into
/// <c>champion_matchup_stats</c> and <c>champion_timeline_lead_stats</c> (#606, made
/// incremental in #811).
///
/// The original (#606) shape recomputed both tables from scratch every cycle via one
/// global self-join GROUP BY over every currently-retained match — cheap right after
/// #606 (~3.5 min on prod, cold cache) but its cost scales with total retained match
/// volume, not with new matches: once prod's 2-patch retention window held ~250k
/// matches it ballooned to ~20+ min/cycle and starved the rest of the ingestion loop
/// (#811). This mirrors <see cref="ChampionPowerspikeAggregationProcess"/> (#694)
/// instead: each match is folded exactly once (gated by
/// <see cref="Match.MatchupLeadAggregated"/>) into additive per-champion rows via
/// <c>ON CONFLICT DO UPDATE SET x = x + EXCLUDED.x</c>, so a cycle's cost scales with
/// matches ingested since the last run, not with the whole retained history. Rows are
/// stored WITHOUT the games floor: the read side folds them to the requested patch
/// scope and applies the floor on the merged total, so the all-patches view floors on
/// the real total. Aged-out patches are never revisited (retention only ever drops
/// whole patches, never a mid-patch straggler — see <c>MatchDataRetentionProcess</c>),
/// so their rows simply freeze once their matches are gone, same as Powerspike.
/// </summary>
public sealed class ChampionMatchupLeadAggregationProcess(
    ILogger<ChampionMatchupLeadAggregationProcess> logger,
    IOptions<MainAnalysisOptions> analysisOptions,
    IOptions<MatchupLeadAggregationOptions> options,
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
        var batchSize = options.Value.MatchBatchSize;
        var maxPerRun = options.Value.MaxMatchesPerRun;
        var aggregatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        var processedMatches = 0;
        var batches = 0;

        while (maxPerRun == 0 || processedMatches < maxPerRun)
        {
            ct.ThrowIfCancellationRequested();

            var take = maxPerRun == 0 ? batchSize : Math.Min(batchSize, maxPerRun - processedMatches);

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);

            // Only matches whose timeline has been ingested carry the snapshots the lead
            // side needs; a match still awaiting its timeline must not be flagged, or its
            // contribution (matchup AND lead) would be lost. MatchIngestion sets
            // TimelineIngested in the same pass as the match row, so this rarely delays
            // anything in practice. The partial index IX_matches_matchup_lead_pending
            // keeps this selection cheap once the backlog is drained.
            var matchIds = await db.Matches
                .AsNoTracking()
                .Where(m => m.QueueId == queueId && !m.MatchupLeadAggregated && m.TimelineIngested)
                .OrderBy(m => m.Id)
                .Take(take)
                .Select(m => m.Id)
                .ToListAsync(ct);

            if (matchIds.Count == 0)
            {
                break;
            }

            await ProcessBatchAsync(db, matchIds, aggregatedAtUtc, ct);

            processedMatches += matchIds.Count;
            batches++;

            if (matchIds.Count < take)
            {
                break;
            }
        }

        logger.LogInformation(
            "Champion matchup/lead aggregation summary: matches={Matches}, batches={Batches}.",
            processedMatches,
            batches);

        return new { matches = processedMatches, batches };
    }

    private static async Task ProcessBatchAsync(
        TrueMainDbContext db,
        List<string> matchIds,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        var patchByMatch = await db.Matches
            .AsNoTracking()
            .Where(m => matchIds.Contains(m.Id))
            .Select(m => new { m.Id, m.GameVersion })
            .ToDictionaryAsync(m => m.Id, m => PatchVersion.Normalize(m.GameVersion), ct);

        var participants = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => matchIds.Contains(p.MatchId) && CanonicalPositions.Contains(p.TeamPosition))
            .Select(p => new ParticipantRow(
                p.MatchId,
                p.ParticipantId,
                p.ChampionId,
                p.TeamId,
                p.TeamPosition,
                p.EloBracket,
                p.RiotAccountId != null,
                p.Win))
            .ToListAsync(ct);

        var snapshotRows = await db.MatchParticipantTimelineSnapshots
            .AsNoTracking()
            .Where(s => matchIds.Contains(s.MatchId) && LeadIntervalMinutes.Contains(s.IntervalMinute))
            .Select(s => new
            {
                s.MatchId,
                s.ParticipantId,
                s.IntervalMinute,
                s.TotalGold,
                Cs = s.MinionsKilled + s.JungleMinionsKilled,
                s.Kills,
                s.Level,
                s.Xp,
                s.DamageToChampions
            })
            .ToListAsync(ct);

        // (MatchId, ParticipantId) -> minute -> snapshot.
        var snapshotsByParticipant = new Dictionary<(string, int), Dictionary<int, SnapshotMinute>>();
        foreach (var s in snapshotRows)
        {
            var key = (s.MatchId, s.ParticipantId);
            if (!snapshotsByParticipant.TryGetValue(key, out var byMinute))
            {
                byMinute = new Dictionary<int, SnapshotMinute>();
                snapshotsByParticipant[key] = byMinute;
            }

            byMinute[s.IntervalMinute] = new SnapshotMinute(s.TotalGold, s.Cs, s.Kills, s.Level, s.Xp, s.DamageToChampions);
        }

        var participantsByMatch = participants
            .GroupBy(p => p.MatchId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var matchups = new Dictionary<MatchupKey, MatchupAccumulator>();
        var leads = new Dictionary<LeadKey, LeadAccumulator>();

        foreach (var (matchId, parts) in participantsByMatch)
        {
            var patch = patchByMatch.GetValueOrDefault(matchId);
            if (string.IsNullOrEmpty(patch))
            {
                continue;
            }

            foreach (var p1 in parts)
            {
                if (!p1.Tracked)
                {
                    continue;
                }

                var opponent = parts.FirstOrDefault(p2 => p2.TeamPosition == p1.TeamPosition && p2.TeamId != p1.TeamId);
                if (opponent is null)
                {
                    continue;
                }

                var matchupKey = new MatchupKey(p1.ChampionId, p1.TeamPosition, opponent.ChampionId, patch, p1.EloBracket);
                if (!matchups.TryGetValue(matchupKey, out var matchupAcc))
                {
                    matchupAcc = new MatchupAccumulator();
                    matchups[matchupKey] = matchupAcc;
                }

                matchupAcc.Games++;
                if (p1.Win)
                {
                    matchupAcc.Wins++;
                }

                if (!snapshotsByParticipant.TryGetValue((matchId, p1.ParticipantId), out var s1)
                    || !snapshotsByParticipant.TryGetValue((matchId, opponent.ParticipantId), out var s2))
                {
                    continue;
                }

                foreach (var minute in LeadIntervalMinutes)
                {
                    if (!s1.TryGetValue(minute, out var m1) || !s2.TryGetValue(minute, out var m2))
                    {
                        continue;
                    }

                    var leadKey = new LeadKey(p1.ChampionId, p1.TeamPosition, patch, p1.EloBracket, minute);
                    if (!leads.TryGetValue(leadKey, out var leadAcc))
                    {
                        leadAcc = new LeadAccumulator();
                        leads[leadKey] = leadAcc;
                    }

                    leadAcc.Games++;
                    leadAcc.GoldDiff += m1.Gold - m2.Gold;
                    leadAcc.CsDiff += m1.Cs - m2.Cs;
                    leadAcc.KillsDiff += m1.Kills - m2.Kills;
                    leadAcc.LevelDiff += m1.Level - m2.Level;
                    leadAcc.XpDiff += m1.Xp - m2.Xp;
                    leadAcc.DamageDiff += m1.Damage - m2.Damage;
                }
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await UpsertMatchupsAsync(db, matchups, aggregatedAtUtc, ct);
        await UpsertLeadsAsync(db, leads, aggregatedAtUtc, ct);

        await db.Matches
            .Where(m => matchIds.Contains(m.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.MatchupLeadAggregated, true), ct);

        await transaction.CommitAsync(ct);
    }

    private static async Task UpsertMatchupsAsync(
        TrueMainDbContext db,
        IReadOnlyDictionary<MatchupKey, MatchupAccumulator> matchups,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        if (matchups.Count == 0)
        {
            return;
        }

        var rows = matchups.ToList();
        const string sql = """
            INSERT INTO champion_matchup_stats
                ("Id", "ChampionId", "TeamPosition", "OpponentChampionId", "Patch", "elo_bracket", "Games", "Wins", "AggregatedAtUtc")
            SELECT gen_random_uuid(), t.champ, t.pos, t.opp, t.patch, t.elo, t.games, t.wins, @aggAt
            FROM unnest(@champs::integer[], @positions::text[], @opponents::integer[], @patches::text[],
                        @elos::text[], @games::integer[], @wins::integer[])
                AS t(champ, pos, opp, patch, elo, games, wins)
            ON CONFLICT ("ChampionId", "TeamPosition", "OpponentChampionId", "Patch", "elo_bracket") DO UPDATE SET
                "Games" = champion_matchup_stats."Games" + EXCLUDED."Games",
                "Wins" = champion_matchup_stats."Wins" + EXCLUDED."Wins",
                "AggregatedAtUtc" = EXCLUDED."AggregatedAtUtc"
            """;

        await db.Database.ExecuteSqlRawAsync(
            sql,
            [
                new NpgsqlParameter("aggAt", aggregatedAtUtc),
                new NpgsqlParameter("champs", rows.Select(r => r.Key.ChampionId).ToArray()),
                new NpgsqlParameter("positions", rows.Select(r => r.Key.TeamPosition).ToArray()),
                new NpgsqlParameter("opponents", rows.Select(r => r.Key.OpponentChampionId).ToArray()),
                new NpgsqlParameter("patches", rows.Select(r => r.Key.Patch).ToArray()),
                new NpgsqlParameter("elos", rows.Select(r => r.Key.EloBracket).ToArray()),
                new NpgsqlParameter("games", rows.Select(r => r.Value.Games).ToArray()),
                new NpgsqlParameter("wins", rows.Select(r => r.Value.Wins).ToArray())
            ],
            ct);
    }

    private static async Task UpsertLeadsAsync(
        TrueMainDbContext db,
        IReadOnlyDictionary<LeadKey, LeadAccumulator> leads,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        if (leads.Count == 0)
        {
            return;
        }

        var rows = leads.ToList();
        const string sql = """
            INSERT INTO champion_timeline_lead_stats
                ("Id", "ChampionId", "TeamPosition", "Patch", "elo_bracket", "IntervalMinute", "Games",
                 "TotalGoldDiff", "TotalCsDiff", "TotalKillsDiff", "TotalLevelDiff", "TotalXpDiff", "TotalDamageDiff", "AggregatedAtUtc")
            SELECT gen_random_uuid(), t.champ, t.pos, t.patch, t.elo, t.minute, t.games,
                   t.gold, t.cs, t.kills, t.level, t.xp, t.damage, @aggAt
            FROM unnest(@champs::integer[], @positions::text[], @patches::text[], @elos::text[], @minutes::integer[],
                        @games::integer[], @gold::bigint[], @cs::bigint[], @kills::bigint[], @level::bigint[],
                        @xp::bigint[], @damage::bigint[])
                AS t(champ, pos, patch, elo, minute, games, gold, cs, kills, level, xp, damage)
            ON CONFLICT ("ChampionId", "TeamPosition", "Patch", "IntervalMinute", "elo_bracket") DO UPDATE SET
                "Games" = champion_timeline_lead_stats."Games" + EXCLUDED."Games",
                "TotalGoldDiff" = champion_timeline_lead_stats."TotalGoldDiff" + EXCLUDED."TotalGoldDiff",
                "TotalCsDiff" = champion_timeline_lead_stats."TotalCsDiff" + EXCLUDED."TotalCsDiff",
                "TotalKillsDiff" = champion_timeline_lead_stats."TotalKillsDiff" + EXCLUDED."TotalKillsDiff",
                "TotalLevelDiff" = champion_timeline_lead_stats."TotalLevelDiff" + EXCLUDED."TotalLevelDiff",
                "TotalXpDiff" = champion_timeline_lead_stats."TotalXpDiff" + EXCLUDED."TotalXpDiff",
                "TotalDamageDiff" = champion_timeline_lead_stats."TotalDamageDiff" + EXCLUDED."TotalDamageDiff",
                "AggregatedAtUtc" = EXCLUDED."AggregatedAtUtc"
            """;

        await db.Database.ExecuteSqlRawAsync(
            sql,
            [
                new NpgsqlParameter("aggAt", aggregatedAtUtc),
                new NpgsqlParameter("champs", rows.Select(r => r.Key.ChampionId).ToArray()),
                new NpgsqlParameter("positions", rows.Select(r => r.Key.TeamPosition).ToArray()),
                new NpgsqlParameter("patches", rows.Select(r => r.Key.Patch).ToArray()),
                new NpgsqlParameter("elos", rows.Select(r => r.Key.EloBracket).ToArray()),
                new NpgsqlParameter("minutes", rows.Select(r => r.Key.IntervalMinute).ToArray()),
                new NpgsqlParameter("games", rows.Select(r => r.Value.Games).ToArray()),
                new NpgsqlParameter("gold", rows.Select(r => r.Value.GoldDiff).ToArray()),
                new NpgsqlParameter("cs", rows.Select(r => r.Value.CsDiff).ToArray()),
                new NpgsqlParameter("kills", rows.Select(r => r.Value.KillsDiff).ToArray()),
                new NpgsqlParameter("level", rows.Select(r => r.Value.LevelDiff).ToArray()),
                new NpgsqlParameter("xp", rows.Select(r => r.Value.XpDiff).ToArray()),
                new NpgsqlParameter("damage", rows.Select(r => r.Value.DamageDiff).ToArray())
            ],
            ct);
    }

    private sealed record ParticipantRow(
        string MatchId,
        int ParticipantId,
        int ChampionId,
        int TeamId,
        string TeamPosition,
        string EloBracket,
        bool Tracked,
        bool Win);

    private readonly record struct SnapshotMinute(int Gold, int Cs, int Kills, int Level, int Xp, int Damage);

    private readonly record struct MatchupKey(int ChampionId, string TeamPosition, int OpponentChampionId, string Patch, string EloBracket);

    private readonly record struct LeadKey(int ChampionId, string TeamPosition, string Patch, string EloBracket, int IntervalMinute);

    private sealed class MatchupAccumulator
    {
        public int Games;
        public int Wins;
    }

    private sealed class LeadAccumulator
    {
        public int Games;
        public long GoldDiff;
        public long CsDiff;
        public long KillsDiff;
        public long LevelDiff;
        public long XpDiff;
        public long DamageDiff;
    }
}
