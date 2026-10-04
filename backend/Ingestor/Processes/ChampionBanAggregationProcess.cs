using Core.Lol.Patches;
using Core.Lol.Ranking;
using Core.Options;
using Data;
using Data.Entities;
using Ingestor.Options;
using Ingestor.Processes.Components.IncrementalFolds;
using Ingestor.Processes.Summaries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

/// <summary>
/// Incrementally folds each match's champion-select bans into
/// <c>champion_ban_stats</c>, plus the match totals those counts are divided by
/// into <c>ban_scope_totals</c> (#920).
///
/// Structurally this is <see cref="ChampionSynergyAggregationProcess"/> again: one
/// fold per match gated by <see cref="Match.BansAggregated"/>, additive rows via
/// <c>ON CONFLICT DO UPDATE SET x = x + EXCLUDED.x</c>, and aged-out patches never
/// revisited so their rows freeze when retention drops their matches (#466). Three
/// things are specific to bans:
///
/// 1. <b>The denominator is stored, not counted.</b> Every other rate on the site
///    divides by something still present at read time; a ban rate divides by "how
///    many matches were there", and matches are retired after a couple of patches.
///    So the fold emits a <see cref="BanScopeTotal"/> row in the same pass, from the
///    same matches, and the read never touches <c>matches</c>.
///
/// 2. <b>A match counts once per elo band it touched, and <c>ALL</c> is stored.</b>
///    A match has no single band — <c>match_participants.elo_bracket</c> is resolved
///    per tracked player — so it is folded into every distinct band among its
///    participants, and separately into the synthetic <see cref="EloBracket.All"/>.
///    Numerator and denominator both do this, so each band's rate reads "share of
///    the matches involving a player at this band that banned X". The bands overlap
///    and therefore cannot be summed back into an unfiltered total, which is exactly
///    why <c>ALL</c> is a stored row here rather than the read-time union it is
///    everywhere else.
///
/// 3. <b>There is no backlog.</b> The other folds ship their flag false and drain
///    the retained history; this one ships it true. Riot payloads are not kept, so a
///    match ingested before #920 has no <see cref="MatchBan"/> rows — folding it
///    would add to the denominator while contributing no bans and deflate every
///    champion's rate. Ban history therefore starts at deploy, and the read surfaces
///    a "since patch X" gap rather than a zero.
///
/// Like the synergy fold there is no <see cref="Match.TimelineIngested"/> gate: bans
/// come from the match payload itself and gating on timelines would drop matches for
/// no benefit.
/// </summary>
public sealed class ChampionBanAggregationProcess(
    ILogger<ChampionBanAggregationProcess> logger,
    IOptions<MainAnalysisOptions> analysisOptions,
    IOptions<BanAggregationOptions> options,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    TimeProvider timeProvider) : IIngestorProcess
{
    private static readonly AdditiveUpsert<KeyValuePair<BanKey, int>> BanStatsUpsert =
        new AdditiveUpsert<KeyValuePair<BanKey, int>>("champion_ban_stats")
            .Key("ChampionId", "integer", r => r.Key.ChampionId)
            .Key("Patch", "text", r => r.Key.Patch)
            .Key("elo_bracket", "text", r => r.Key.EloBracket)
            .Sum("Bans", "integer", r => r.Value);

    private static readonly AdditiveUpsert<KeyValuePair<ScopeKey, int>> ScopeTotalsUpsert =
        new AdditiveUpsert<KeyValuePair<ScopeKey, int>>("ban_scope_totals")
            .Key("Patch", "text", r => r.Key.Patch)
            .Key("elo_bracket", "text", r => r.Key.EloBracket)
            .Sum("Matches", "integer", r => r.Value);

    public string Name => "ChampionBanAggregation";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var queueId = (int)analysisOptions.Value.QueueId;
        var batchSize = options.Value.MatchBatchSize;
        var maxPerRun = options.Value.MaxMatchesPerRun;
        var aggregatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        var banRows = 0;
        var scopeRows = 0;

        // IX_matches_bans_pending keeps the selection an index scan. It starts empty (the
        // flag is backfilled to true) and only ever holds the tail ingested since the
        // previous run.
        var (processedMatches, batches) = await IncrementalMatchFold.RunAsync(
            dbContextFactory,
            queueId,
            m => !m.BansAggregated,
            batchSize,
            maxPerRun,
            async (db, matchIds, token) =>
            {
                var written = await ProcessBatchAsync(db, matchIds, aggregatedAtUtc, token);
                banRows += written.BanRows;
                scopeRows += written.ScopeRows;
            },
            ct);

        logger.LogInformation(
            "Champion ban aggregation summary: matches={Matches}, batches={Batches}, "
            + "banRows={BanRows}, scopeRows={ScopeRows}.",
            processedMatches,
            batches,
            banRows,
            scopeRows);

        return new BanAggregationSummary(processedMatches, batches, banRows, scopeRows);
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

        // Only the bands matter here, not the participants: a match is folded once
        // per distinct band that appeared in it. Participants still awaiting elo
        // enrichment carry an empty band and are skipped, so a match nobody has been
        // stamped for lands in ALL alone.
        var bracketRows = await db.MatchParticipants
            .AsNoTracking()
            .Where(p => matchIds.Contains(p.MatchId) && p.EloBracket != "")
            .Select(p => new { p.MatchId, p.EloBracket })
            .Distinct()
            .ToListAsync(ct);

        var bracketsByMatch = bracketRows
            .GroupBy(row => row.MatchId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.EloBracket).ToList());

        var banRows = await db.MatchBans
            .AsNoTracking()
            .Where(b => matchIds.Contains(b.MatchId))
            .Select(b => new { b.MatchId, b.ChampionId })
            .Distinct()
            .ToListAsync(ct);

        var bansByMatch = banRows
            .GroupBy(row => row.MatchId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.ChampionId).ToList());

        var bans = new Dictionary<BanKey, int>();
        var totals = new Dictionary<ScopeKey, int>();

        foreach (var matchId in matchIds)
        {
            var patch = patchByMatch.GetValueOrDefault(matchId);
            if (string.IsNullOrEmpty(patch))
            {
                continue;
            }

            // ALL always, then every band seen in the match. A match with no ban rows
            // still lands in the totals — it is a match in which this champion was not
            // banned, which is precisely what the denominator counts.
            var brackets = new List<string> { EloBracket.All };
            if (bracketsByMatch.TryGetValue(matchId, out var matchBrackets))
            {
                brackets.AddRange(matchBrackets);
            }

            var championIds = bansByMatch.GetValueOrDefault(matchId) ?? [];

            foreach (var bracket in brackets)
            {
                var scope = new ScopeKey(patch, bracket);
                totals[scope] = totals.GetValueOrDefault(scope) + 1;

                foreach (var championId in championIds)
                {
                    var key = new BanKey(championId, patch, bracket);
                    bans[key] = bans.GetValueOrDefault(key) + 1;
                }
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await BanStatsUpsert.ExecuteAsync(db, bans, aggregatedAtUtc, ct);
        await ScopeTotalsUpsert.ExecuteAsync(db, totals, aggregatedAtUtc, ct);

        await db.Matches
            .Where(m => matchIds.Contains(m.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.BansAggregated, true), ct);

        await transaction.CommitAsync(ct);

        return new WrittenRows(bans.Count, totals.Count);
    }

    private readonly record struct BanKey(int ChampionId, string Patch, string EloBracket);

    private readonly record struct ScopeKey(string Patch, string EloBracket);

    private readonly record struct WrittenRows(int BanRows, int ScopeRows);
}
