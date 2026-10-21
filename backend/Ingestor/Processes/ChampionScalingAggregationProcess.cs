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
/// Incrementally folds each match into <c>champion_scaling_stats</c> (#1368): per
/// (champion, position, patch, elo band, game-length bucket), the games and wins of the
/// champion cohort — the numbers behind the champion page's scaling chart.
///
/// <para>
/// The scaling read used to fold <c>match_participants</c> live on every request, and on
/// production a cold read took 30–45 s for a popular champion (Gwen TOP, Thresh UTILITY,
/// Jhin BOTTOM on 2026-10-05), on a chart drawn on every champion page. Its output never
/// changes once a match is in, so it is folded once here and the read sums a handful of
/// rows instead.
/// </para>
///
/// <para>
/// Structurally <see cref="ChampionMatchupLeadAggregationProcess"/> without the opponent:
/// one fold per match gated by <see cref="Match.ScalingAggregated"/>, additive rows via
/// <c>ON CONFLICT DO UPDATE SET x = x + EXCLUDED.x</c>, aged-out patches never revisited
/// (#466), and the champion side drawn from <see cref="ChampionCohort"/> — the same
/// population the live read composed, so the panel did not change meaning when it moved
/// here. The flag ships <c>false</c>: the table is created empty and the retained history
/// drains through the fold once, in bounded batches. No <see cref="Match.TimelineIngested"/>
/// gate, like the synergy fold: a game's length and result come with the match itself.
/// </para>
/// </summary>
public sealed class ChampionScalingAggregationProcess(
    ILogger<ChampionScalingAggregationProcess> logger,
    IOptions<MainAnalysisOptions> analysisOptions,
    IOptions<ScalingAggregationOptions> options,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    TimeProvider timeProvider) : IIngestorProcess
{
    private static readonly AdditiveUpsert<KeyValuePair<ScalingKey, ScalingAccumulator>> ScalingUpsert =
        new AdditiveUpsert<KeyValuePair<ScalingKey, ScalingAccumulator>>("champion_scaling_stats")
            .Key("ChampionId", "integer", r => r.Key.ChampionId)
            .Key("TeamPosition", "text", r => r.Key.TeamPosition)
            .Key("Patch", "text", r => r.Key.Patch)
            .Key("EloBracket", "text", r => r.Key.EloBracket)
            .Key("Bucket", "integer", r => r.Key.Bucket)
            .Sum("Games", "integer", r => r.Value.Games)
            .Sum("Wins", "integer", r => r.Value.Wins);

    public string Name => "ChampionScalingAggregation";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var queueId = (int)analysisOptions.Value.QueueId;
        var batchSize = options.Value.MatchBatchSize;
        var maxPerRun = options.Value.MaxMatchesPerRun;
        var aggregatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        var participants = 0;
        var rows = 0;

        // IX_matches_scaling_pending keeps the selection an index scan: every retained
        // match on the first runs, the tail ingested since the previous run afterwards.
        var (processedMatches, batches) = await IncrementalMatchFold.RunAsync(
            dbContextFactory,
            queueId,
            m => !m.ScalingAggregated,
            batchSize,
            maxPerRun,
            async (db, matchIds, token) =>
            {
                var written = await ProcessBatchAsync(db, matchIds, aggregatedAtUtc, token);
                participants += written.Participants;
                rows += written.Rows;
            },
            ct);

        logger.LogInformation(
            "Champion scaling aggregation summary: matches={Matches}, batches={Batches}, "
            + "participants={Participants}, rows={Rows}.",
            processedMatches,
            batches,
            participants,
            rows);

        return new ScalingAggregationSummary(processedMatches, batches, participants, rows);
    }

    private static async Task<WrittenRows> ProcessBatchAsync(
        TrueMainDbContext db,
        List<string> matchIds,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        // The stored Patch column rather than PatchVersion.Normalize: it is what the live
        // read filtered on, so a game version the two spell differently ("16.04.1") still
        // lands on the row that read would have counted it under.
        var patchByMatch = await db.Matches
            .AsNoTracking()
            .Where(m => matchIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Patch })
            .ToDictionaryAsync(m => m.Id, m => m.Patch, ct);

        var bucketByMatch = await ChampionScalingBuckets.LoadAsync(db, matchIds, ct);

        // Only the champion side matters here — there is no opponent or partner — so the
        // participant load is narrowed to the canonical lanes and membership is tested per
        // row against the cohort, exactly as the matchup fold does.
        var cohort = await ChampionCohort.LoadAsync(db, matchIds, ct);

        var rows = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => matchIds.Contains(p.MatchId) && ChampionCohort.CanonicalPositions.Contains(p.TeamPosition))
            .Select(p => new ParticipantRow(
                p.MatchId,
                p.ParticipantId,
                p.ChampionId,
                p.TeamPosition,
                p.EloBracket,
                p.Win))
            .ToListAsync(ct);

        var scaling = new Dictionary<ScalingKey, ScalingAccumulator>();
        var folded = 0;

        foreach (var row in rows)
        {
            var patch = patchByMatch.GetValueOrDefault(row.MatchId);
            if (string.IsNullOrEmpty(patch)
                || !bucketByMatch.TryGetValue(row.MatchId, out var bucket)
                || !cohort.Includes(row.MatchId, row.ParticipantId))
            {
                continue;
            }

            var key = new ScalingKey(row.ChampionId, row.TeamPosition, patch, row.EloBracket, bucket);
            if (!scaling.TryGetValue(key, out var accumulator))
            {
                accumulator = new ScalingAccumulator();
                scaling[key] = accumulator;
            }

            folded++;
            accumulator.Games++;
            if (row.Win)
            {
                accumulator.Wins++;
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await ScalingUpsert.ExecuteAsync(db, scaling, aggregatedAtUtc, ct);

        await db.Matches
            .Where(m => matchIds.Contains(m.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ScalingAggregated, true), ct);

        await transaction.CommitAsync(ct);

        return new WrittenRows(folded, scaling.Count);
    }

    private sealed record ParticipantRow(
        string MatchId,
        int ParticipantId,
        int ChampionId,
        string TeamPosition,
        string EloBracket,
        bool Win);

    private readonly record struct ScalingKey(
        int ChampionId,
        string TeamPosition,
        string Patch,
        string EloBracket,
        int Bucket);

    private readonly record struct WrittenRows(int Participants, int Rows);

    private sealed class ScalingAccumulator
    {
        public int Games;
        public int Wins;
    }
}
