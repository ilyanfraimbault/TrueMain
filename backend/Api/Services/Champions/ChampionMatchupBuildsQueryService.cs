using Core.Lol.Patches;
using Core.Lol.Ranking;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// The champion page scoped to one lane opponent (#923): every build section recomputed
/// from the games where this champion actually faced that opponent.
///
/// <para>
/// <b>Why this cannot read the aggregates.</b> <c>champion_matchup_stats</c> carries the
/// opponent dimension but only games and wins — no build data. The pattern aggregates
/// carry the build data but are grained on
/// (account, champion, patch, platform, queue, position, elo), with no opponent at all.
/// So a matchup-scoped build has no aggregate to read and is folded live from
/// <c>match_participants</c>, which is the only place the two facts meet.
/// </para>
///
/// <para>
/// <b>Cost.</b> Measured on production (patch 16.13, the richest): per champion ×
/// opponent × position across every elo, the median pair holds 4 games, p90 = 79,
/// p99 = 355 and the largest 1 562. Folding a few hundred participants is cheap — the
/// live path is affordable precisely because a matchup slice is small. The cap below
/// exists for the tail, not for the common case.
/// </para>
/// </summary>
public sealed class ChampionMatchupBuildsQueryService(
    TrueMainDbContext db,
    ParticipantBuildFactsLoader factsLoader,
    IOptions<MainAnalysisOptions> mainAnalysisOptions,
    ILogger<ChampionMatchupBuildsQueryService> logger) : IChampionMatchupBuildsQueryService
{
    /// <summary>
    /// Newest games folded for one matchup. Above the measured maximum (1 562) with room
    /// for a corpus that grows, so in practice it bounds nothing today; it is here so an
    /// unexpectedly huge pair cannot turn a page view into an unbounded fold.
    /// </summary>
    private const int MaxMatchupGames = 2_000;

    public async Task<ChampionResponse?> GetAsync(
        int championId,
        int opponentChampionId,
        string? patch,
        string position,
        string? eloBracket,
        CancellationToken ct)
    {
        if (championId <= 0 || opponentChampionId <= 0 || string.IsNullOrWhiteSpace(position))
        {
            return null;
        }

        var queueId = (int)mainAnalysisOptions.Value.QueueId;
        var requestedPatch = PatchFilter.Normalize(patch);
        var bracketFilter = EloBracket.ResolveFilterOrEmpty(eloBracket);
        var resolvedBracket = EloBracket.Normalize(eloBracket) ?? EloBracket.All;

        // With no patch asked for, resolve the newest one this matchup was played on and
        // scope to it — rather than spanning every patch retention still holds. Two
        // reasons: the page's patch selector shows a single patch, so a response silently
        // mixing two would put a number under a label that does not describe it (measured
        // on preprod: 955 games unscoped against 105 for the patch shown); and builds do
        // not survive a patch, so pooling them across one is not a bigger sample, it is a
        // different question.
        // Resolved on the same elo filter the main query below applies. Without this, an
        // elo whose games all sit on an older patch than the matchup's overall newest would
        // resolve a patch it has no rows on and come back "no data" for an elo that genuinely
        // has some — the exact kind of number/filter mismatch this whole method exists to
        // avoid, just moved one level down to the elo dimension.
        var normalizedPatch = requestedPatch ?? await ResolveNewestPatchAsync(
            championId, opponentChampionId, position, queueId, bracketFilter, ct);

        var participants = db.MatchParticipants
            .AsNoTracking()
            .Where(p => p.ChampionId == championId && p.TeamPosition == position);

        if (bracketFilter is not null)
        {
            participants = participants.Where(p => bracketFilter.Contains(p.EloBracket));
        }

        // Same self-join ResolveNewestPatchAsync runs below, patch filter aside — pulled
        // into JoinMatchupMatches so the two queries can't drift on what "facing X" means
        // (flagged in review: they used to be copy-pasted, one edit away from disagreeing).
        var keys = await JoinMatchupMatches(participants, opponentChampionId, position, queueId, normalizedPatch)
            .Select(row => new { row.MatchId, row.ParticipantId, row.GameStartTimeUtc })
            // Newest first: when the cap does bite, the games it keeps are the ones whose
            // builds are still current.
            .OrderByDescending(row => row.GameStartTimeUtc)
            .ThenBy(row => row.MatchId)
            .Take(MaxMatchupGames)
            .Select(row => new ParticipantKey(row.MatchId, row.ParticipantId))
            .ToListAsync(ct);

        if (keys.Count == 0)
        {
            // No game of this matchup in the retained window. Null rather than an empty
            // build list, so the caller renders "no data for this matchup" instead of a
            // page that looks aggregated and happens to be blank.
            return null;
        }

        // Unweighted: within a matchup no game is "closer" than another, unlike the
        // composition search where similarity to the requested draft is the ranking.
        var facts = await factsLoader.LoadAsync(keys, championId, position, weightFor: null, ct);

        var builds = LiveBuildVariationAggregator.Aggregate(facts);
        var wins = facts.Count(fact => fact.Win);

        logger.LogInformation(
            "champion-matchup-builds championId={ChampionId} opponentId={OpponentId} position={Position} "
            + "patch={Patch} elo={Elo} games={Games} builds={Builds}",
            championId, opponentChampionId, position, normalizedPatch ?? "all", resolvedBracket,
            facts.Count, builds.Count);

        return new ChampionResponse
        {
            ChampionId = championId,
            // Always the patch actually covered: the page reconciles its selector against
            // this, and an empty string would read as "no patch" and clear the filter.
            Patch = normalizedPatch ?? string.Empty,
            Position = position,
            EloBracket = resolvedBracket,
            // The matchup slice is exactly the games asked for, so its coverage of itself
            // is total; thinness is carried by the game counts on every row instead.
            EloCoverage = 1d,
            MinSampleMet = true,
            TotalGames = facts.Count,
            TotalWins = wins,
            Builds = builds,
        };
    }

    /// <summary>
    /// The newest patch this matchup was played on, or null when it was never played.
    /// One index-ordered read of the pair's newest game.
    /// </summary>
    private async Task<string?> ResolveNewestPatchAsync(
        int championId,
        int opponentChampionId,
        string position,
        int queueId,
        IReadOnlyList<string>? bracketFilter,
        CancellationToken ct)
    {
        var participants = db.MatchParticipants
            .AsNoTracking()
            .Where(p => p.ChampionId == championId && p.TeamPosition == position);

        if (bracketFilter is not null)
        {
            participants = participants.Where(p => bracketFilter.Contains(p.EloBracket));
        }

        var newestVersion = await JoinMatchupMatches(participants, opponentChampionId, position, queueId, patch: null)
            .OrderByDescending(row => row.GameStartTimeUtc)
            .Select(row => row.GameVersion)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrEmpty(newestVersion) ? null : PatchVersion.Normalize(newestVersion);
    }

    /// <summary>
    /// The self-join deciding which participants "faced" <paramref name="opponentChampionId"/>:
    /// same match, opponent champion at <paramref name="position"/>, on the other team, in
    /// <paramref name="queueId"/> and — when given — starting with <paramref name="patch"/>.
    ///
    /// <para>
    /// Shared by <see cref="GetAsync"/> and <see cref="ResolveNewestPatchAsync"/> so the two
    /// queries cannot silently disagree on what "facing X" means; before this they were two
    /// copies of the same join, one edit away from drifting. Same shape as the matchup
    /// winrate (#90), reused here to pick games rather than to count them.
    /// </para>
    /// </summary>
    private IQueryable<MatchupMatchRow> JoinMatchupMatches(
        IQueryable<Data.Entities.MatchParticipant> participants,
        int opponentChampionId,
        string position,
        int queueId,
        string? patch)
    {
        var patchPrefix = PatchFilter.Prefix(patch);

        return participants
            .Join(
                db.MatchParticipants.AsNoTracking().Where(o =>
                    o.ChampionId == opponentChampionId && o.TeamPosition == position),
                p => p.MatchId,
                o => o.MatchId,
                (p, o) => new { Participant = p, Opponent = o })
            .Where(pair => pair.Opponent.TeamId != pair.Participant.TeamId)
            .Join(
                db.Matches.AsNoTracking().Where(m => m.QueueId == queueId
                    && (patchPrefix == null || EF.Functions.Like(m.GameVersion, patchPrefix))),
                pair => pair.Participant.MatchId,
                m => m.Id,
                (pair, m) => new MatchupMatchRow
                {
                    MatchId = pair.Participant.MatchId,
                    ParticipantId = pair.Participant.ParticipantId,
                    GameStartTimeUtc = m.GameStartTimeUtc,
                    GameVersion = m.GameVersion,
                });
    }

    // Object-initializer syntax, not a positional record: Npgsql's EF provider fails to
    // translate a later OrderBy/Select over a positional constructor call re-embedded from
    // the Join's resultSelector ("could not be translated"). Property-init form translates
    // cleanly and keeps the same immutable-record ergonomics everywhere else.
    private sealed record MatchupMatchRow
    {
        public required string MatchId { get; init; }
        public required int ParticipantId { get; init; }
        public required DateTime GameStartTimeUtc { get; init; }
        public required string GameVersion { get; init; }
    }
}
