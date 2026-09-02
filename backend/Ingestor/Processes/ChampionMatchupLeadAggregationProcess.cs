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
/// <c>champion_matchup_stats</c> (#606, made incremental in #811). The sibling
/// <c>champion_timeline_lead_stats</c> aggregate this process used to write was
/// dropped along with the "Lead vs role opponent" chart in #889.
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
/// </summary>
public sealed class ChampionMatchupLeadAggregationProcess(
    ILogger<ChampionMatchupLeadAggregationProcess> logger,
    IOptions<MainAnalysisOptions> analysisOptions,
    IOptions<MatchupLeadAggregationOptions> options,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    TimeProvider timeProvider) : IIngestorProcess
{
    public string Name => "ChampionMatchupLeadAggregation";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
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

            // The TimelineIngested gate outlived the lead aggregate it was added for
            // (#889): matchup rows need only participants. It is kept deliberately, so
            // the aggregate's cohort stays what it has always been — dropping it would
            // suddenly fold every timeline-less match ever ingested, shifting historical
            // matchup winrates. MatchIngestion sets TimelineIngested in the same pass as
            // the match row, so this rarely delays anything in practice. The partial
            // index IX_matches_matchup_lead_pending keeps this selection cheap once the
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

            await ProcessBatchAsync(db, matchIds, aggregatedAtUtc, ct);

            processedMatches += matchIds.Count;
            batches++;

            if (matchIds.Count < take)
            {
                break;
            }
        }

        logger.LogInformation(
            "Champion matchup aggregation summary: matches={Matches}, batches={Batches}.",
            processedMatches,
            batches);

        return new MatchAggregationSummary(processedMatches, batches);
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

        var participantsByMatch = participants
            .GroupBy(p => p.MatchId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var matchups = new Dictionary<MatchupKey, MatchupAccumulator>();

        foreach (var (matchId, parts) in participantsByMatch)
        {
            var patch = patchByMatch.GetValueOrDefault(matchId);
            if (string.IsNullOrEmpty(patch))
            {
                continue;
            }

            foreach (var p1 in parts)
            {
                if (!cohort.Includes(matchId, p1.ParticipantId))
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
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await UpsertMatchupsAsync(db, matchups, aggregatedAtUtc, ct);

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

    private sealed record ParticipantRow(
        string MatchId,
        int ParticipantId,
        int ChampionId,
        int TeamId,
        string TeamPosition,
        string EloBracket,
        bool Win);

    private readonly record struct MatchupKey(int ChampionId, string TeamPosition, int OpponentChampionId, string Patch, string EloBracket);

    private sealed class MatchupAccumulator
    {
        public int Games;
        public int Wins;
    }
}
