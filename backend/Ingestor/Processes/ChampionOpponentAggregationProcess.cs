using Core.Lol.Patches;
using Core.Options;
using Data;
using Data.Aggregation;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes.Components.IncrementalFolds;
using Ingestor.Processes.Summaries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

/// <summary>
/// Incrementally pre-aggregates how tracked players fared against each enemy champion
/// into <c>champion_opponent_stats</c>, plus the marginal rates that metric is measured
/// against into <c>champion_opponent_baseline_stats</c> (#1713) — what lets the draft
/// weigh the enemy team beyond the lane opponent.
///
/// <para>
/// <see cref="ChampionSynergyAggregationProcess"/>'s design, with enemies where it has
/// teammates: each match folded exactly once (gated by <see cref="Match.OpponentAggregated"/>)
/// into additive rows, the tracked side drawn from the shared
/// <see cref="ChampionCohort"/> and the enemy side left to whoever played, five
/// pairs per tracked seat. Its own flag and process rather than four more lines in the
/// synergy fold: the synergy flag is already set on every retained match, so only a
/// separate gate lets this aggregate drain the retained history without refolding — and
/// double-counting — the synergies.
/// </para>
///
/// <para>
/// Baselines from the same fold: <c>SELF</c> for the tracked seat, <c>ENEMY</c> for each
/// enemy, both counting the tracked player's result. Every enemy is paired, the lane
/// opponent included; the draft read leaves the lane to <c>champion_matchup_stats</c>.
/// </para>
/// </summary>
public sealed class ChampionOpponentAggregationProcess(
    ILogger<ChampionOpponentAggregationProcess> logger,
    IOptions<MainAnalysisOptions> analysisOptions,
    IOptions<OpponentAggregationOptions> options,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    TimeProvider timeProvider) : IIngestorProcess
{
    private static readonly AdditiveUpsert<KeyValuePair<OpponentKey, Accumulator>> PairsUpsert =
        new AdditiveUpsert<KeyValuePair<OpponentKey, Accumulator>>("champion_opponent_stats")
            .Key("ChampionId", "integer", r => r.Key.ChampionId)
            .Key("TeamPosition", "text", r => r.Key.TeamPosition)
            .Key("OpponentChampionId", "integer", r => r.Key.OpponentChampionId)
            .Key("OpponentPosition", "text", r => r.Key.OpponentPosition)
            .Key("Patch", "text", r => r.Key.Patch)
            .Key("EloBracket", "text", r => r.Key.EloBracket)
            .Sum("Games", "integer", r => r.Value.Games)
            .Sum("Wins", "integer", r => r.Value.Wins);

    private static readonly AdditiveUpsert<KeyValuePair<BaselineKey, Accumulator>> BaselinesUpsert =
        new AdditiveUpsert<KeyValuePair<BaselineKey, Accumulator>>("champion_opponent_baseline_stats")
            .Key("ChampionId", "integer", r => r.Key.ChampionId)
            .Key("TeamPosition", "text", r => r.Key.TeamPosition)
            .Key("Side", "text", r => r.Key.Side)
            .Key("Patch", "text", r => r.Key.Patch)
            .Key("EloBracket", "text", r => r.Key.EloBracket)
            .Sum("Games", "integer", r => r.Value.Games)
            .Sum("Wins", "integer", r => r.Value.Wins);

    public string Name => "ChampionOpponentAggregation";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var queueId = (int)analysisOptions.Value.QueueId;
        var batchSize = options.Value.MatchBatchSize;
        var maxPerRun = options.Value.MaxMatchesPerRun;
        var aggregatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        var pairRows = 0;
        var baselineRows = 0;

        // IX_matches_opponent_pending keeps the selection an index scan: the whole
        // retained table on day one, the pending tail once the backlog has drained.
        var (processedMatches, batches) = await IncrementalMatchFold.RunAsync(
            dbContextFactory,
            queueId,
            m => !m.OpponentAggregated,
            batchSize,
            maxPerRun,
            async (db, matchIds, token) =>
            {
                var written = await ProcessBatchAsync(db, matchIds, aggregatedAtUtc, token);
                pairRows += written.PairRows;
                baselineRows += written.BaselineRows;
            },
            ct);

        logger.LogInformation(
            "Champion opponent aggregation summary: matches={Matches}, batches={Batches}, "
            + "pairRows={PairRows}, baselineRows={BaselineRows}.",
            processedMatches,
            batches,
            pairRows,
            baselineRows);

        return new OpponentAggregationSummary(processedMatches, batches, pairRows, baselineRows);
    }

    private static async Task<WrittenRows> ProcessBatchAsync(
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

        // Who may sit on the queried side of a pairing. Every participant below is
        // loaded regardless — an off-cohort player is still somebody's partner — and
        // membership is tested per row against this set (ChampionCohort).
        var cohort = await ChampionCohort.LoadAsync(db, matchIds, ct);

        // A participant with an empty or garbage TeamPosition cannot be placed in a
        // composition, so it is excluded on both sides of the pair rather than stored
        // as an enemy nobody can ask for. Same canonical set the cohort tests, so the
        // two sides cannot drift apart.
        var participants = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => matchIds.Contains(p.MatchId)
                && ChampionCohort.CanonicalPositions.Contains(p.TeamPosition))
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

        var pairs = new Dictionary<OpponentKey, Accumulator>();
        var baselines = new Dictionary<BaselineKey, Accumulator>();

        foreach (var (matchId, parts) in participantsByMatch)
        {
            var patch = patchByMatch.GetValueOrDefault(matchId);
            if (string.IsNullOrEmpty(patch))
            {
                continue;
            }

            foreach (var self in parts)
            {
                // The queried side, and only it: a main of this champion, in a game
                // that lasted. The enemy rows below are emitted from this seat, so the
                // enemy side keeps taking whoever played.
                if (!cohort.Includes(matchId, self.ParticipantId))
                {
                    continue;
                }

                Add(
                    baselines,
                    new BaselineKey(self.ChampionId, self.TeamPosition, OpponentBaselineSide.Self, patch, self.EloBracket),
                    self.Win);

                foreach (var enemy in parts)
                {
                    if (enemy.TeamId == self.TeamId)
                    {
                        continue;
                    }

                    // Keyed on the tracked player's elo band, pair and baseline alike, so a
                    // rank-filtered read selects the same games on both — the synergy rule.
                    Add(
                        pairs,
                        new OpponentKey(
                            self.ChampionId,
                            self.TeamPosition,
                            enemy.ChampionId,
                            enemy.TeamPosition,
                            patch,
                            self.EloBracket),
                        self.Win);

                    Add(
                        baselines,
                        new BaselineKey(enemy.ChampionId, enemy.TeamPosition, OpponentBaselineSide.Enemy, patch, self.EloBracket),
                        self.Win);
                }
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await PairsUpsert.ExecuteAsync(db, pairs, aggregatedAtUtc, ct);
        await BaselinesUpsert.ExecuteAsync(db, baselines, aggregatedAtUtc, ct);

        await db.Matches
            .Where(m => matchIds.Contains(m.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.OpponentAggregated, true), ct);

        await transaction.CommitAsync(ct);

        return new WrittenRows(pairs.Count, baselines.Count);
    }

    private static void Add<TKey>(Dictionary<TKey, Accumulator> target, TKey key, bool win)
        where TKey : notnull
    {
        if (!target.TryGetValue(key, out var accumulator))
        {
            accumulator = new Accumulator();
            target[key] = accumulator;
        }

        accumulator.Games++;
        if (win)
        {
            accumulator.Wins++;
        }
    }

    private sealed record ParticipantRow(
        string MatchId,
        int ParticipantId,
        int ChampionId,
        int TeamId,
        string TeamPosition,
        string EloBracket,
        bool Win);

    private readonly record struct OpponentKey(
        int ChampionId,
        string TeamPosition,
        int OpponentChampionId,
        string OpponentPosition,
        string Patch,
        string EloBracket);

    private readonly record struct BaselineKey(
        int ChampionId,
        string TeamPosition,
        string Side,
        string Patch,
        string EloBracket);

    private readonly record struct WrittenRows(int PairRows, int BaselineRows);

    private sealed class Accumulator
    {
        public int Games;
        public int Wins;
    }
}
