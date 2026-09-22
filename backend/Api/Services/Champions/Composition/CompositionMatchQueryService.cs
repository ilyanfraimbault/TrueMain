using Core.Lol.Patches;
using Core.Lol.Ranking;
using Core.Options;
using Data;
using Data.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Matchups;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Composition;

/// <summary>
/// Finds the historical games most similar to a requested (possibly partial)
/// draft for a champion at a position — the selection stage of the
/// composition-based build recommender (#563). Hard filter is champion +
/// position, plus the role opponent when the draft pins one; the rest of the
/// composition ranks, it never filters.
/// </summary>
public interface ICompositionMatchQueryService
{
    Task<CompositionMatchesResult> FindTopMatchesAsync(
        CompositionSearchCriteria criteria,
        CancellationToken ct);
}

/// <summary>
/// Live composition match search over the FULL participant pool — harvested
/// rows included, unlike the tracked-only champion-page reads, because a
/// build is valid regardless of whether its player is tracked (served by the
/// non-filtered <c>IX_match_participants_champion_position_full</c> index).
/// Two-stage shape mirroring the live path of
/// <see cref="ChampionMatchupQueryService"/>: SQL narrows to the candidates and
/// joins their nine co-participants as slim slot rows; similarity scoring and
/// selection then run in memory on the pure
/// <see cref="CompositionSimilarityScorer"/>, which keeps the weights
/// unit-testable and cheap to tune.
///
/// <para>
/// <b>The role opponent is filtered in the database (#1659).</b> It was a hard
/// requirement applied in memory, after the champion's whole retained history had been
/// loaded and sorted — so a request paid for tens of thousands of games to keep the few
/// hundred of one matchup, and cold responses were measured between 1 s and 43 s on
/// production. With the filter pushed into <see cref="MatchupParticipantQuery"/> the pool
/// is the matchup itself: 4 games at the median, 355 at p99, 1 562 at the measured
/// maximum. Nothing is truncated at that size, which is why the selection then keeps
/// every game of the matchup rather than a top-K of it.
/// </para>
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
            options.RoleOpponentWeight, options.EnemyWeight, options.AllyWeight);

        // Same queue cast as the sibling champion reads, and the same LIKE
        // prefix bridge from normalised patch input to the full GameVersion
        // stored on matches.
        var queueId = (int)analysisOptions.Value.QueueId;
        var normalizedPatch = PatchFilter.Normalize(criteria.Patch);

        var bands = EloBracket.ResolveFilterOrEmpty(criteria.EloBracket);

        var participants = db.MatchParticipants
            .AsNoTracking()
            .Where(p => p.ChampionId == criteria.ChampionId && p.TeamPosition == criteria.Position);
        if (bands is not null)
        {
            participants = participants.Where(p => bands.Contains(p.EloBracket));
        }

        // The role opponent, when pinned, is a hard requirement (#563 rev): a build only
        // transfers between games of the same matchup, so a candidate without it is
        // excluded rather than merely out-scored. Since #1659 the exclusion happens in
        // SQL, which is what makes the request affordable.
        var matchupRequested = criteria.Enemies.TryGetValue(criteria.Position, out var roleOpponentId);

        var candidates = matchupRequested
            ? MatchupParticipantQuery.Facing(
                db, participants, roleOpponentId, criteria.Position, queueId, normalizedPatch)
            : participants.Join(
                db.Matches.AsNoTracking().Where(m =>
                    m.QueueId == queueId
                    && (normalizedPatch == null || m.Patch == normalizedPatch)),
                p => p.MatchId,
                m => m.Id,
                (p, m) => new MatchupParticipantRow
                {
                    MatchId = p.MatchId,
                    ParticipantId = p.ParticipantId,
                    TeamId = p.TeamId,
                    Win = p.Win,
                    Puuid = p.Puuid,
                    GameStartTimeUtc = m.GameStartTimeUtc,
                    GameVersion = m.GameVersion,
                });

        // Newest first, bounded by the guardrail. Patches are chronological, so recency
        // ordering already fills from the current patch before reaching into the previous
        // one — there is no separate patch ordering to apply.
        var cappedCandidates = candidates
            .OrderByDescending(x => x.GameStartTimeUtc)
            .ThenBy(x => x.MatchId)
            .Take(options.CandidatePoolCap);

        // One round-trip: the capped candidate subquery joined to its nine
        // co-participants, projected to the slim columns scoring needs.
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
                    c.Puuid,
                    c.GameStartTimeUtc,
                    c.GameVersion,
                    OtherTeamId = o.TeamId,
                    OtherPosition = o.TeamPosition,
                    OtherChampionId = o.ChampionId,
                })
            .ToListAsync(ct);

        // Roster of accounts that main this champion — the whole set for one
        // champion is bounded (served by the partial IsMain index) and lets the
        // selection prefer games actually piloted by a main over incidental
        // games by non-mains, per-Puuid, in memory.
        var mainPuuids = (await db.MainChampionStats
                .AsNoTracking()
                .Where(s => s.ChampionId == criteria.ChampionId && s.IsMain && s.IsActive)
                .Select(s => s.Puuid)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var maxScore = CompositionSimilarityScorer.MaxScore(criteria, weights);

        // The patch every vote is weighed against: the one asked for, or the newest the
        // pool actually holds. Read off the rows already in hand rather than resolved
        // with its own query — asking the database for "this champion's newest patch"
        // means ordering its whole history by match time, which is the very shape this
        // change exists to remove. Only the *relative* weight matters: where a pool spans
        // two patches the newer one is correctly favoured, and where it sits entirely on
        // one, every vote scales alike and the aggregation is unchanged.
        var currentPatch = normalizedPatch ?? NewestPatch(rows.Select(r => r.GameVersion));

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
                var isTruemain = mainPuuids.Contains(first.Puuid);
                return new CompositionMatchRef
                {
                    MatchId = g.Key.MatchId,
                    ParticipantId = g.Key.ParticipantId,
                    Score = score,
                    Win = first.Win,
                    GameStartTimeUtc = first.GameStartTimeUtc,
                    Puuid = first.Puuid,
                    IsTruemain = isTruemain,
                    IsCurrentPatch = currentPatch is not null
                        && PatchVersion.Normalize(first.GameVersion) == currentPatch,
                };
            })
            .ToList();

        // Two-tier ordering, unchanged: games piloted by a main of the champion first,
        // similarity within each tier, recency breaking ties. The provenance drawer
        // (#940) pages this very order, so it stays deterministic whether or not the
        // selection is truncated below.
        var ranked = scored
            .OrderByDescending(m => m.IsTruemain)
            .ThenByDescending(m => m.Score)
            .ThenByDescending(m => m.GameStartTimeUtc)
            .ThenBy(m => m.MatchId, StringComparer.Ordinal);

        // With the matchup filtered in SQL the pool IS the answer — every game of it
        // counts, weighted by patch and pilot in the aggregation rather than ranked out
        // of the sample. Without a pinned opponent the pool is the champion's recent
        // games instead, where past the first hundred similarity decides nothing and
        // hydrating the rest would cost more than it informs, so the top-K still applies.
        var selected = matchupRequested
            ? ranked.ToList()
            : ranked.Take(options.TopK).ToList();

        return new CompositionMatchesResult
        {
            ChampionId = criteria.ChampionId,
            Position = criteria.Position,
            Patch = normalizedPatch,
            CandidatePoolSize = scored.Count,
            TruemainGameCount = selected.Count(m => m.IsTruemain),
            MaxPossibleScore = maxScore,
            MeanSimilarity = maxScore == 0 || selected.Count == 0
                ? 0d
                : selected.Average(m => (double)m.Score / maxScore),
            MatchupRequested = matchupRequested,
            MatchupFound = !matchupRequested || selected.Count > 0,
            Matches = selected,
        };
    }

    /// <summary>
    /// Highest <c>major.minor</c> among the candidates' game versions, or null when none
    /// parses. Compared as versions, never as strings — <c>16.9</c> sorts after
    /// <c>16.10</c> alphabetically, which would hand the older patch the heavier vote.
    /// </summary>
    private static string? NewestPatch(IEnumerable<string> gameVersions)
    {
        PatchVersion? newest = null;
        foreach (var gameVersion in gameVersions)
        {
            if (PatchVersion.TryParse(gameVersion, out var parsed)
                && (newest is null || parsed > newest.Value))
            {
                newest = parsed;
            }
        }

        return newest?.ToMajorMinor();
    }
}
