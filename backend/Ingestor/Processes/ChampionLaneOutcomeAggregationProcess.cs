using Core.Lol.Patches;
using Core.Options;
using Data;
using Data.Aggregation;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes.Summaries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ingestor.Processes;

/// <summary>
/// Folds each match's lane outcome — did the champion end 15 minutes ahead of its lane
/// opponent — into the lane counters on <c>champion_matchup_stats</c> (#919), so the
/// champion page can show "won the lane" beside "won the game".
///
/// <para>
/// Structurally this is <see cref="ChampionMatchupLeadAggregationProcess"/>'s fold with
/// one extra input: the 15-minute timeline snapshot of both lane participants. It runs
/// immediately after it, over the same matches, so the row its <c>ON CONFLICT</c>
/// targets already exists.
/// </para>
///
/// <para>
/// <b>The issue's premise had gone stale.</b> #919 assumed the per-matchup gold leads at
/// 15 minutes were already aggregated, from #606/#595. They are not: #889 dropped that
/// aggregate along with the "Lead vs role opponent" chart, and this process's sibling
/// now folds participant rows only. What survives is the raw
/// <see cref="MatchParticipantTimelineSnapshot"/> at the canonical marks — snapshot
/// pruning keeps 5/10/15/20/30 — which is enough, and is why the fold flag can ship
/// false and pick up the whole retained window rather than starting at deploy.
/// </para>
///
/// <para>
/// <b>Three counters, not two.</b> A gold *threshold* creates a third outcome: lanes
/// inside the band are neither won nor lost. Storing wins and losses separately keeps
/// them out of the ratio instead of silently counting them as losses, and keeps
/// <c>LaneGames</c> — matches where a lane could be judged at all — distinct from the
/// matchup's <c>Games</c>. A match with no ingested timeline, or one that ended before
/// the 15-minute mark, is a game but not a judgeable lane; dividing by <c>Games</c>
/// would understate every lane win rate by the share of those.
/// </para>
///
/// <para>
/// <b>And the magnitude beside them (#976).</b> The counters say whether a lane was
/// won, never by how much: +180 and +1800 gold are the same row to them. The same
/// pass sums the raw gap into <c>LaneGoldDiffSum</c> over its own
/// <c>LaneGoldDiffGames</c>, which is what lets the read side band a matchup as even
/// / good / dominant — and lets those band edges move without re-folding anything.
/// </para>
///
/// <para>
/// <b>And the experience beside the gold (#1111).</b> The same 15-minute snapshot
/// already read for gold carries <c>Xp</c>, summed into <c>LaneXpDiffSum</c> over its
/// own <c>LaneXpDiffGames</c>. Two numbers rather than one because they answer
/// different questions and routinely disagree: gold is who bought more, XP is who is
/// bigger, and a lane won on kills while losing waves reads as a gold lead over an XP
/// deficit — a lead the next all-in reverses. Deriving either from the other would
/// erase exactly the case worth showing.
/// </para>
/// </summary>
public sealed class ChampionLaneOutcomeAggregationProcess(
    ILogger<ChampionLaneOutcomeAggregationProcess> logger,
    IOptions<MainAnalysisOptions> analysisOptions,
    IOptions<LaneOutcomeAggregationOptions> options,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    TimeProvider timeProvider) : IIngestorProcess
{
    private const int LaneOutcomeMinute = 15;

    private static readonly string[] CanonicalPositions = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

    public string Name => "ChampionLaneOutcomeAggregation";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var queueId = (int)analysisOptions.Value.QueueId;
        var settings = options.Value;
        var maxPerRun = settings.MaxMatchesPerRun;

        var processedMatches = 0;
        var judgedLanes = 0;
        var rows = 0;
        var batches = 0;
        var aggregatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        while (maxPerRun == 0 || processedMatches < maxPerRun)
        {
            ct.ThrowIfCancellationRequested();

            var take = maxPerRun == 0
                ? settings.MatchBatchSize
                : Math.Min(settings.MatchBatchSize, maxPerRun - processedMatches);

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);

            // IX_matches_lane_outcome_pending keeps this an index scan. It covers the
            // whole retained table on day one and shrinks as the backlog drains.
            var matchIds = await db.Matches
                .AsNoTracking()
                .Where(m => m.QueueId == queueId && !m.LaneOutcomeAggregated)
                .OrderBy(m => m.Id)
                .Take(take)
                .Select(m => m.Id)
                .ToListAsync(ct);

            if (matchIds.Count == 0)
            {
                break;
            }

            var written = await ProcessBatchAsync(db, matchIds, settings.GoldLeadThreshold, aggregatedAtUtc, ct);

            processedMatches += matchIds.Count;
            judgedLanes += written.JudgedLanes;
            rows += written.Rows;
            batches++;

            if (matchIds.Count < take)
            {
                break;
            }
        }

        logger.LogInformation(
            "Champion lane outcome aggregation summary: matches={Matches}, batches={Batches}, "
            + "judgedLanes={JudgedLanes}, rows={Rows}, threshold={Threshold}.",
            processedMatches,
            batches,
            judgedLanes,
            rows,
            settings.GoldLeadThreshold);

        return new LaneOutcomeAggregationSummary(
            processedMatches, batches, judgedLanes, rows, settings.GoldLeadThreshold);
    }

    private static async Task<WrittenRows> ProcessBatchAsync(
        TrueMainDbContext db,
        List<string> matchIds,
        int goldLeadThreshold,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        var patchByMatch = await db.Matches
            .AsNoTracking()
            .Where(m => matchIds.Contains(m.Id))
            .Select(m => new { m.Id, m.GameVersion })
            .ToDictionaryAsync(m => m.Id, m => PatchVersion.Normalize(m.GameVersion), ct);

        var cohort = await MatchupCohort.LoadAsync(db, matchIds, ct);

        var participants = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => matchIds.Contains(p.MatchId) && CanonicalPositions.Contains(p.TeamPosition))
            .Select(p => new ParticipantRow(
                p.MatchId,
                p.ParticipantId,
                p.ChampionId,
                p.TeamId,
                p.TeamPosition,
                p.EloBracket))
            .ToListAsync(ct);

        // The gold reading both sides are compared on. Keyed per (match, participant);
        // a match whose timeline was never ingested simply has no rows here, and its
        // lanes are then not judgeable — counted in neither LaneGames nor the outcomes.
        var readingsAt15 = await db.MatchParticipantTimelineSnapshots
            .AsNoTracking()
            .Where(s => matchIds.Contains(s.MatchId) && s.IntervalMinute == LaneOutcomeMinute)
            .Select(s => new { s.MatchId, s.ParticipantId, s.TotalGold, s.Xp })
            .ToDictionaryAsync(s => (s.MatchId, s.ParticipantId), s => new Reading(s.TotalGold, s.Xp), ct);

        var participantsByMatch = participants
            .GroupBy(p => p.MatchId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var accumulators = new Dictionary<MatchupKey, LaneAccumulator>();
        var judgedLanes = 0;

        foreach (var (matchId, parts) in participantsByMatch)
        {
            var patch = patchByMatch.GetValueOrDefault(matchId);
            if (string.IsNullOrEmpty(patch))
            {
                continue;
            }

            foreach (var self in parts)
            {
                // Same asymmetry as the matchup fold it sits beside: the (champion,
                // position) side is a main of that champion, the opponent is whoever
                // was in that lane. Keeping the two folds' cohorts identical — hence
                // the shared MatchupCohort — is what lets the read put lane WR and
                // game WR on the same row.
                if (!cohort.Contains(new MatchupCohortKey(matchId, self.ParticipantId)))
                {
                    continue;
                }

                var opponent = parts.FirstOrDefault(other =>
                    other.TeamPosition == self.TeamPosition && other.TeamId != self.TeamId);
                if (opponent is null)
                {
                    continue;
                }

                if (!readingsAt15.TryGetValue((matchId, self.ParticipantId), out var selfReading)
                    || !readingsAt15.TryGetValue((matchId, opponent.ParticipantId), out var opponentReading))
                {
                    // No 15-minute reading for one of the two: a real game, but not a
                    // lane anyone can call. Left out of every counter rather than
                    // guessed at.
                    continue;
                }

                var key = new MatchupKey(self.ChampionId, self.TeamPosition, opponent.ChampionId, patch, self.EloBracket);
                if (!accumulators.TryGetValue(key, out var accumulator))
                {
                    accumulator = new LaneAccumulator();
                    accumulators[key] = accumulator;
                }

                accumulator.LaneGames++;
                judgedLanes++;

                var lead = selfReading.TotalGold - opponentReading.TotalGold;

                // The magnitude behind the outcome (#976). The threshold below answers
                // "was the lane won"; this answers "by how much", which the counters
                // cannot: ±180 and ±1800 are the same row to them. Kept on its own
                // counter so rows folded before #976 — which have lane outcomes and no
                // sum — read as "unknown gap" instead of "even lane".
                accumulator.LaneGoldDiffSum += lead;
                accumulator.LaneGoldDiffGames++;

                // The other half of "who is ahead" (#1111), on its own counter. Gold
                // says who bought more, XP says who is bigger, and a lane won on kills
                // while losing waves shows one without the other — the disagreement is
                // the reading, so neither may be derived from the other.
                accumulator.LaneXpDiffSum += selfReading.Xp - opponentReading.Xp;
                accumulator.LaneXpDiffGames++;

                if (lead > goldLeadThreshold)
                {
                    accumulator.LaneWins++;
                }
                else if (lead < -goldLeadThreshold)
                {
                    accumulator.LaneLosses++;
                }
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await UpsertAsync(db, accumulators, aggregatedAtUtc, ct);

        await db.Matches
            .Where(m => matchIds.Contains(m.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LaneOutcomeAggregated, true), ct);

        await transaction.CommitAsync(ct);

        return new WrittenRows(judgedLanes, accumulators.Count);
    }

    private static async Task UpsertAsync(
        TrueMainDbContext db,
        IReadOnlyDictionary<MatchupKey, LaneAccumulator> accumulators,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        if (accumulators.Count == 0)
        {
            return;
        }

        var rows = accumulators.ToList();

        // Additive on the lane columns only: Games/Wins belong to the sibling matchup
        // fold and are inserted as 0 here purely to satisfy the not-null columns on the
        // rare path where this fold sees a matchup first. The read floors on Games, so a
        // row that only ever got lane counters stays invisible rather than showing a
        // lane win rate for a matchup with no recorded games.
        const string sql = """
            INSERT INTO champion_matchup_stats
                ("Id", "ChampionId", "TeamPosition", "OpponentChampionId", "Patch", "elo_bracket",
                 "Games", "Wins", "LaneGames", "LaneWins", "LaneLosses",
                 "LaneGoldDiffSum", "LaneGoldDiffGames",
                 "LaneXpDiffSum", "LaneXpDiffGames", "AggregatedAtUtc")
            SELECT gen_random_uuid(), t.champ, t.pos, t.opp, t.patch, t.elo,
                   0, 0, t.lane_games, t.lane_wins, t.lane_losses,
                   t.gold_diff_sum, t.gold_diff_games,
                   t.xp_diff_sum, t.xp_diff_games, @aggAt
            FROM unnest(@champs::integer[], @positions::text[], @opponents::integer[], @patches::text[],
                        @elos::text[], @laneGames::integer[], @laneWins::integer[], @laneLosses::integer[],
                        @goldDiffSums::bigint[], @goldDiffGames::integer[],
                        @xpDiffSums::bigint[], @xpDiffGames::integer[])
                AS t(champ, pos, opp, patch, elo, lane_games, lane_wins, lane_losses,
                     gold_diff_sum, gold_diff_games, xp_diff_sum, xp_diff_games)
            ON CONFLICT ("ChampionId", "TeamPosition", "OpponentChampionId", "Patch", "elo_bracket") DO UPDATE SET
                "LaneGames" = champion_matchup_stats."LaneGames" + EXCLUDED."LaneGames",
                "LaneWins" = champion_matchup_stats."LaneWins" + EXCLUDED."LaneWins",
                "LaneLosses" = champion_matchup_stats."LaneLosses" + EXCLUDED."LaneLosses",
                "LaneGoldDiffSum" = champion_matchup_stats."LaneGoldDiffSum" + EXCLUDED."LaneGoldDiffSum",
                "LaneGoldDiffGames" = champion_matchup_stats."LaneGoldDiffGames" + EXCLUDED."LaneGoldDiffGames",
                "LaneXpDiffSum" = champion_matchup_stats."LaneXpDiffSum" + EXCLUDED."LaneXpDiffSum",
                "LaneXpDiffGames" = champion_matchup_stats."LaneXpDiffGames" + EXCLUDED."LaneXpDiffGames",
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
                new NpgsqlParameter("laneGames", rows.Select(r => r.Value.LaneGames).ToArray()),
                new NpgsqlParameter("laneWins", rows.Select(r => r.Value.LaneWins).ToArray()),
                new NpgsqlParameter("laneLosses", rows.Select(r => r.Value.LaneLosses).ToArray()),
                new NpgsqlParameter("goldDiffSums", rows.Select(r => r.Value.LaneGoldDiffSum).ToArray()),
                new NpgsqlParameter("goldDiffGames", rows.Select(r => r.Value.LaneGoldDiffGames).ToArray()),
                new NpgsqlParameter("xpDiffSums", rows.Select(r => r.Value.LaneXpDiffSum).ToArray()),
                new NpgsqlParameter("xpDiffGames", rows.Select(r => r.Value.LaneXpDiffGames).ToArray())
            ],
            ct);
    }

    private sealed record ParticipantRow(
        string MatchId,
        int ParticipantId,
        int ChampionId,
        int TeamId,
        string TeamPosition,
        string EloBracket);

    private readonly record struct MatchupKey(
        int ChampionId,
        string TeamPosition,
        int OpponentChampionId,
        string Patch,
        string EloBracket);

    private readonly record struct WrittenRows(int JudgedLanes, int Rows);

    /// <summary>The 15-minute snapshot both gaps are measured from.</summary>
    private readonly record struct Reading(int TotalGold, int Xp);

    private sealed class LaneAccumulator
    {
        public int LaneGames;
        public int LaneWins;
        public int LaneLosses;
        public long LaneGoldDiffSum;
        public int LaneGoldDiffGames;
        public long LaneXpDiffSum;
        public int LaneXpDiffGames;
    }
}
