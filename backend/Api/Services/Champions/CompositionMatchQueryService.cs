using Core.Lol.Patches;
using Core.Lol.Ranking;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Live composition match search over the FULL participant pool — harvested
/// rows included, unlike the tracked-only champion-page reads, because a
/// build is valid regardless of whether its player is tracked (served by the
/// non-filtered <c>IX_match_participants_champion_position_full</c> index).
/// Two-stage shape mirroring the live path of
/// <see cref="ChampionMatchupQueryService"/>: SQL narrows to the most recent
/// candidates (bounded by the pool cap — the scan is single-threaded in prod)
/// and joins their nine co-participants as slim slot rows; similarity scoring
/// and top-K selection then run in memory on the pure
/// <see cref="CompositionSimilarityScorer"/>, which keeps the weights
/// unit-testable and cheap to tune.
/// </summary>
public sealed class CompositionMatchQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> analysisOptions,
    IOptions<CompositionSearchOptions> searchOptions)
    : ICompositionMatchQueryService
{
    public async Task<CompositionMatchesResult> FindTopMatchesAsync(
        CompositionSearchCriteria criteria,
        CancellationToken ct)
    {
        var options = searchOptions.Value;
        var weights = new CompositionScoreWeights(
            options.LaneOpponentWeight, options.EnemyWeight, options.AllyWeight);

        // Same queue cast as the sibling champion reads, and the same LIKE
        // prefix bridge from normalised patch input to the full GameVersion
        // stored on matches.
        var queueId = (int)analysisOptions.Value.QueueId;
        var normalizedPatch = string.IsNullOrWhiteSpace(criteria.Patch)
            ? null
            : PatchVersion.TryParse(criteria.Patch, out var parsed) ? parsed.ToMajorMinor() : null;
        var patchPrefix = normalizedPatch is null ? null : $"{normalizedPatch}.%";

        var bands = EloBracket.ResolveFilter(criteria.EloBracket);

        // Candidate rows: this champion at this position over the full pool,
        // most recent first, bounded by the pool cap. Recency is the right
        // truncation: similarity is only meaningful within the current meta,
        // and the cap bounds both the SQL join and the in-memory pass.
        var candidates = db.MatchParticipants
            .AsNoTracking()
            .Where(p => p.ChampionId == criteria.ChampionId && p.TeamPosition == criteria.Position);
        if (bands is not null)
        {
            candidates = candidates.Where(p => bands.Contains(p.EloBracket));
        }

        var cappedCandidates = candidates
            .Join(
                db.Matches.Where(m =>
                    m.QueueId == queueId
                    && (patchPrefix == null || EF.Functions.Like(m.GameVersion, patchPrefix))),
                p => p.MatchId,
                m => m.Id,
                (p, m) => new { p.MatchId, p.ParticipantId, p.TeamId, p.Win, m.GameStartTimeUtc })
            .OrderByDescending(x => x.GameStartTimeUtc)
            .Take(options.CandidatePoolCap);

        // One round-trip: the capped candidate subquery joined to its nine
        // co-participants, projected to the slim columns scoring needs. At the
        // default cap this is tens of thousands of small rows — deliberately
        // far from the load-everything shape that caused the pattern-agg OOM.
        var rows = await cappedCandidates
            .SelectMany(
                c => db.MatchParticipants.Where(o =>
                    o.MatchId == c.MatchId && o.ParticipantId != c.ParticipantId),
                (c, o) => new
                {
                    c.MatchId,
                    c.ParticipantId,
                    c.TeamId,
                    c.Win,
                    c.GameStartTimeUtc,
                    OtherTeamId = o.TeamId,
                    OtherPosition = o.TeamPosition,
                    OtherChampionId = o.ChampionId,
                })
            .ToListAsync(ct);

        var maxScore = CompositionSimilarityScorer.MaxScore(criteria, weights);

        // The lane opponent, when pinned, is a hard requirement (#563 rev):
        // a build only transfers between games of the same matchup, so a
        // candidate without it is filtered out instead of merely out-scored.
        var matchupRequested = criteria.Enemies.TryGetValue(criteria.Position, out var laneOpponentId);

        var scored = rows
            .GroupBy(r => (r.MatchId, r.ParticipantId))
            .Select(g =>
            {
                var first = g.First();
                var slots = g
                    .Select(r => new CompositionSlot(
                        r.OtherTeamId != first.TeamId, r.OtherPosition, r.OtherChampionId))
                    .ToList();
                var score = CompositionSimilarityScorer.Score(criteria, weights, slots);
                return new
                {
                    HasMatchup = !matchupRequested || slots.Any(s =>
                        s.IsEnemy && s.TeamPosition == criteria.Position && s.ChampionId == laneOpponentId),
                    Match = new CompositionMatchRef
                    {
                        MatchId = g.Key.MatchId,
                        ParticipantId = g.Key.ParticipantId,
                        Score = score,
                        Win = first.Win,
                        GameStartTimeUtc = first.GameStartTimeUtc,
                    },
                };
            })
            .ToList();

        var selected = scored
            .Where(m => m.HasMatchup)
            .Select(m => m.Match)
            .OrderByDescending(m => m.Score)
            .ThenByDescending(m => m.GameStartTimeUtc)
            .Take(options.TopK)
            .ToList();

        return new CompositionMatchesResult
        {
            ChampionId = criteria.ChampionId,
            Position = criteria.Position,
            Patch = normalizedPatch,
            CandidatePoolSize = scored.Count,
            MaxPossibleScore = maxScore,
            MeanSimilarity = maxScore == 0 || selected.Count == 0
                ? 0d
                : selected.Average(m => (double)m.Score / maxScore),
            MatchupRequested = matchupRequested,
            MatchupFound = !matchupRequested || selected.Count > 0,
            Matches = selected,
        };
    }
}
