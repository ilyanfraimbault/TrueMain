using Core.Lol.Lane;
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
/// Incrementally pre-aggregates the champion page's lane-matchup slice into
/// <c>champion_matchup_stats</c> (#606, made incremental in #811) — both the game
/// counters (<c>Games</c> / <c>Wins</c>) and the lane counters the 15-minute snapshots
/// decide (#919, #976, #1111). The sibling <c>champion_timeline_lead_stats</c> aggregate
/// this process used to write was dropped along with the "Lead vs role opponent" chart
/// in #889.
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
///
/// <para>
/// The champion side of every row is a <b>main of that champion</b>
/// (<see cref="ChampionCohort"/>), the same cohort the champion aggregates on the page
/// around the panel count. It used to be the wider "any account we know", which put
/// 3.2× more games behind the matchups panel than behind the header directly above it.
/// </para>
///
/// <para>
/// <b>One pass, not two (#1445).</b> The lane counters used to be a second process
/// (<c>ChampionLaneOutcomeAggregation</c>) with its own pending flag, folding the same
/// matches a moment later. The row key includes <c>match_participants.elo_bracket</c>,
/// which is <em>mutable</em> — <see cref="MatchParticipantEloBracketEnrichmentProcess"/>
/// stamps it once the account's first rank snapshot lands — and since #1362 the fetch
/// lane ingests matches while the aggregate lane is mid-run. So a match could be folded
/// on the lane side while its bracket was still empty and on the game side after the
/// stamping, putting the two halves of one match on two different rows: on preprod that
/// left 10 rows carrying more <c>LaneGames</c> than <c>Games</c> — arithmetically
/// impossible, and flagged as such by the admin's data-quality check — beside real-band
/// rows whose lane sample was quietly short. Folding both sides in one pass, off one
/// flag and one read of the participants, makes the split unrepresentable rather than
/// rare.
/// </para>
///
/// <para>
/// <b>Three lane counters, not two.</b> A gold *threshold* creates a third outcome:
/// lanes inside the band are neither won nor lost. Storing wins and losses separately
/// keeps them out of the ratio instead of silently counting them as losses, and keeps
/// <c>LaneGames</c> — matches where a lane could be judged at all — distinct from the
/// matchup's <c>Games</c>. A match that ended before the 15-minute mark is a game but
/// not a judgeable lane; dividing by <c>Games</c> would understate every lane win rate
/// by the share of those. So <c>Games >= LaneGames</c> is the normal reading, and the
/// reverse is the defect above.
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
public sealed class ChampionMatchupLeadAggregationProcess(
    ILogger<ChampionMatchupLeadAggregationProcess> logger,
    IOptions<MainAnalysisOptions> analysisOptions,
    IOptions<MatchupLeadAggregationOptions> options,
    IOptions<LaneOutcomeAggregationOptions> laneOptions,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    TimeProvider timeProvider) : IIngestorProcess
{
    private const int LaneOutcomeMinute = 15;

    public string Name => "ChampionMatchupLeadAggregation";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var queueId = (int)analysisOptions.Value.QueueId;
        var batchSize = options.Value.MatchBatchSize;
        var maxPerRun = options.Value.MaxMatchesPerRun;
        var goldLeadThreshold = laneOptions.Value.GoldLeadThreshold;
        var aggregatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        var processedMatches = 0;
        var judgedLanes = 0;
        var rows = 0;
        var batches = 0;

        while (maxPerRun == 0 || processedMatches < maxPerRun)
        {
            ct.ThrowIfCancellationRequested();

            var take = maxPerRun == 0 ? batchSize : Math.Min(batchSize, maxPerRun - processedMatches);

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);

            // The TimelineIngested gate outlived the lead aggregate it was added for
            // (#889), and the lane counters gave it a second reason to stay: they read
            // the 15-minute snapshots, and folding a match whose timeline has not
            // arrived yet would flag it as done while contributing no lane at all
            // (#1223). For the game counters it also keeps the aggregate's cohort what
            // it has always been — dropping it would suddenly fold every timeline-less
            // match ever ingested, shifting historical matchup winrates. MatchIngestion
            // sets TimelineIngested in the same pass as the match row, so this rarely
            // delays anything in practice. The partial index
            // IX_matches_matchup_lead_pending keeps this selection cheap once the
            // backlog is drained.
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

            var written = await ProcessBatchAsync(db, matchIds, goldLeadThreshold, aggregatedAtUtc, ct);

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
            "Champion matchup aggregation summary: matches={Matches}, batches={Batches}, "
            + "judgedLanes={JudgedLanes}, rows={Rows}, threshold={Threshold}.",
            processedMatches,
            batches,
            judgedLanes,
            rows,
            goldLeadThreshold);

        return new MatchupAggregationSummary(
            processedMatches, batches, judgedLanes, rows, goldLeadThreshold);
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

        // Who may contribute a champion-side row. Every participant below is loaded
        // regardless — an off-cohort player is still somebody's lane opponent — and
        // membership is tested per row against this set. See ChampionCohort for why the
        // gate is "main of this champion" rather than "account we know", and why a
        // remake is not a game.
        var cohort = await ChampionCohort.LoadAsync(db, matchIds, ct);

        var participants = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => matchIds.Contains(p.MatchId) && ChampionCohort.CanonicalPositions.Contains(p.TeamPosition))
            .Select(p => new ParticipantRow(
                p.MatchId,
                p.ParticipantId,
                p.ChampionId,
                p.TeamId,
                p.TeamPosition,
                p.EloBracket,
                p.Win))
            .ToListAsync(ct);

        // The gold and XP reading both sides are compared on. Keyed per (match,
        // participant); a game that ended before the 15-minute mark simply has no rows
        // here, and its lanes are then not judgeable — counted in neither LaneGames nor
        // the outcomes, while the game itself still counts in Games.
        var readingsAt15 = await db.MatchParticipantTimelineSnapshots
            .AsNoTracking()
            .Where(s => matchIds.Contains(s.MatchId) && s.IntervalMinute == LaneOutcomeMinute)
            .Select(s => new { s.MatchId, s.ParticipantId, s.TotalGold, s.Xp })
            .ToDictionaryAsync(s => (s.MatchId, s.ParticipantId), s => new Reading(s.TotalGold, s.Xp), ct);

        var participantsByMatch = participants
            .GroupBy(p => p.MatchId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var matchups = new Dictionary<MatchupKey, MatchupAccumulator>();
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
                // The asymmetry of the pairing: the (champion, position) side is a main
                // of that champion, the opponent is whoever was in that lane.
                if (!cohort.Includes(matchId, self.ParticipantId))
                {
                    continue;
                }

                var opponent = parts.FirstOrDefault(other =>
                    other.TeamPosition == self.TeamPosition && other.TeamId != self.TeamId);
                if (opponent is null)
                {
                    continue;
                }

                var key = new MatchupKey(
                    self.ChampionId, self.TeamPosition, opponent.ChampionId, patch, self.EloBracket);
                if (!matchups.TryGetValue(key, out var accumulator))
                {
                    accumulator = new MatchupAccumulator();
                    matchups[key] = accumulator;
                }

                accumulator.Games++;
                if (self.Win)
                {
                    accumulator.Wins++;
                }

                if (!readingsAt15.TryGetValue((matchId, self.ParticipantId), out var selfReading)
                    || !readingsAt15.TryGetValue((matchId, opponent.ParticipantId), out var opponentReading))
                {
                    // No 15-minute reading for one of the two: a real game, but not a
                    // lane anyone can call. Left out of the lane counters rather than
                    // guessed at — and, because this is the same accumulator, it is the
                    // *same row* whose Games it just counted.
                    continue;
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

                // Shared with the API's live pass over a composition's sampled games
                // (#1117): "the lane was won" must mean one thing across the site.
                switch (LaneOutcomeRules.Judge(lead, goldLeadThreshold))
                {
                    case LaneStanding.Won:
                        accumulator.LaneWins++;
                        break;
                    case LaneStanding.Lost:
                        accumulator.LaneLosses++;
                        break;
                }
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await UpsertMatchupsAsync(db, matchups, aggregatedAtUtc, ct);

        await db.Matches
            .Where(m => matchIds.Contains(m.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.MatchupLeadAggregated, true), ct);

        await transaction.CommitAsync(ct);

        return new WrittenRows(judgedLanes, matchups.Count);
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
                ("Id", "ChampionId", "TeamPosition", "OpponentChampionId", "Patch", "elo_bracket",
                 "Games", "Wins", "LaneGames", "LaneWins", "LaneLosses",
                 "LaneGoldDiffSum", "LaneGoldDiffGames",
                 "LaneXpDiffSum", "LaneXpDiffGames", "AggregatedAtUtc")
            SELECT gen_random_uuid(), t.champ, t.pos, t.opp, t.patch, t.elo,
                   t.games, t.wins, t.lane_games, t.lane_wins, t.lane_losses,
                   t.gold_diff_sum, t.gold_diff_games,
                   t.xp_diff_sum, t.xp_diff_games, @aggAt
            FROM unnest(@champs::integer[], @positions::text[], @opponents::integer[], @patches::text[],
                        @elos::text[], @games::integer[], @wins::integer[],
                        @laneGames::integer[], @laneWins::integer[], @laneLosses::integer[],
                        @goldDiffSums::bigint[], @goldDiffGames::integer[],
                        @xpDiffSums::bigint[], @xpDiffGames::integer[])
                AS t(champ, pos, opp, patch, elo, games, wins, lane_games, lane_wins, lane_losses,
                     gold_diff_sum, gold_diff_games, xp_diff_sum, xp_diff_games)
            ON CONFLICT ("ChampionId", "TeamPosition", "OpponentChampionId", "Patch", "elo_bracket") DO UPDATE SET
                "Games" = champion_matchup_stats."Games" + EXCLUDED."Games",
                "Wins" = champion_matchup_stats."Wins" + EXCLUDED."Wins",
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
                new NpgsqlParameter("games", rows.Select(r => r.Value.Games).ToArray()),
                new NpgsqlParameter("wins", rows.Select(r => r.Value.Wins).ToArray()),
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
        string EloBracket,
        bool Win);

    private readonly record struct MatchupKey(int ChampionId, string TeamPosition, int OpponentChampionId, string Patch, string EloBracket);

    private readonly record struct WrittenRows(int JudgedLanes, int Rows);

    /// <summary>The 15-minute snapshot both gaps are measured from.</summary>
    private readonly record struct Reading(int TotalGold, int Xp);

    private sealed class MatchupAccumulator
    {
        public int Games;
        public int Wins;
        public int LaneGames;
        public int LaneWins;
        public int LaneLosses;
        public long LaneGoldDiffSum;
        public int LaneGoldDiffGames;
        public long LaneXpDiffSum;
        public int LaneXpDiffGames;
    }
}
