using Core.Lol.Map;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Aggregation;

/// <summary>
/// Identifies one participant row inside a batch of matches. Composite because
/// <c>ParticipantId</c> is Riot's 1-10 slot number and is only unique within a match.
/// </summary>
public readonly record struct ChampionCohortKey(string MatchId, int ParticipantId);

/// <summary>
/// The one definition of "this participant counts for the champion they played" that
/// every champion-page fold and every live champion read composes: <b>the account is
/// tracked, the match is not a remake, the position is canonical, and
/// <c>main_champion_stats.IsMain</c> says this player is a main of that champion</b>.
/// The folds load it per batch (<see cref="LoadAsync"/>); the API's live reads — item
/// timings, scaling, trio synergies, the mains comparison pool — compose it as a query
/// (<see cref="Members"/>), and the reads that pick another champion side on purpose
/// still take their matches from <see cref="Games(TrueMainDbContext, int, string?)"/>.
/// Both spellings are the one private predicate below (#1365).
///
/// <para>
/// <b>Why it lives in <c>Data</c> and why it is shared.</b> Several folds write the panels
/// stacked on one champion page — <c>ChampionMatchupLeadAggregationProcess</c> and
/// <c>ChampionLaneOutcomeAggregationProcess</c> (the matchups panel) and
/// <c>ChampionSynergyAggregationProcess</c> (synergies) — beside a header read
/// from <c>champion_aggregate_scopes</c>. Whenever one of them restates the cohort in
/// its own words the numbers drift apart while still looking comparable: #1087 measured
/// 14 576 games behind the matchups panel against 4 605 behind the header immediately
/// above it, on the same champion, lane and patch — a factor of 3.2 — because the folds
/// gated on <c>RiotAccountId != null</c> ("an account we know") while the aggregate
/// gated on <c>IsMain</c> ("a main of this champion", the site's premise). #1087 fixed
/// the two matchup folds; the synergy fold carried the same defect until #1365 pointed
/// it here too.
/// </para>
///
/// <para>
/// <b>The queried side only.</b> The opponent of a matchup and the partner of a synergy
/// pairing are whoever was in that game, main or not, tracked or not. Narrowing them would
/// measure "how this champion's mains do against/with that champion's mains", a different
/// and much thinner question —
/// and for synergy it would break the maths outright, since the expected value is built
/// from a partner side drawn from the general population (#922).
/// </para>
///
/// <para>
/// <b>Mains, not the widened population.</b> The aggregate behind the header carries
/// both populations since #1346 and its reads choose, defaulting to truemains; these
/// tables carry no such dimension, so they answer for mains only and the matchups
/// panel already rejects <c>truemainsOnly=false</c> rather than mislabelling them.
/// Gating here on <c>IsMain</c> is therefore what makes the panels agree with the
/// header's default — the number a reader compares them against.
/// </para>
///
/// <para>
/// <b>IsActive is deliberately not tested.</b> It retires a main from *future ingestion*
/// (#900); applying it here would silently drop already-folded history the moment a
/// player stopped playing the champion, and would part company with the aggregate
/// header, which does not test it either.
/// </para>
///
/// <para>
/// <b>Remakes.</b> Two signals, either one enough: Riot's own
/// <c>gameEndedInEarlySurrender</c>, stored on the match since #1364
/// (<c>matches.EndedInEarlySurrender</c>), and a duration floor —
/// <see cref="MinimumGameDurationSeconds"/> — which still judges every match ingested
/// before the flag was, and any remake Riot failed to flag. Both are held here rather
/// than restated per fold.
/// </para>
///
/// <para>
/// <b>Not retroactive.</b> Every fold is additive and frozen patches can never be
/// recomputed (#466), so tightening this gate corrects nothing already written: the
/// migration that ships a change deletes the affected rows for the *live* patches and
/// re-arms their per-match flags, and patches whose matches are gone keep the numbers
/// they were folded with.
/// </para>
/// </summary>
public static class ChampionCohort
{
    /// <summary>
    /// A match shorter than this is a remake — the pre-5-minute vote every fold used to
    /// count as a game, each one deciding for itself whether to. The vote cannot open
    /// before 3 minutes nor the game end much past 4, so the floor separates the two
    /// populations cleanly; it stands beside Riot's <c>gameEndedInEarlySurrender</c>
    /// because matches ingested before #1364 do not carry the flag.
    /// </summary>
    public const int MinimumGameDurationSeconds = 300;

    /// <summary>
    /// The five canonical lane positions. A participant with an empty or garbage
    /// <c>TeamPosition</c> cannot be placed in a composition, a matchup or a lane, so it
    /// is not part of any champion cohort — on either side of a pairing.
    /// </summary>
    /// <remarks>
    /// <see cref="LanePositions.All"/> materialised as an array: this is the form EF Core
    /// translates to <c>= ANY(@p)</c> when a fold or a read filters on it.
    /// </remarks>
    public static readonly string[] CanonicalPositions = [.. LanePositions.All];

    /// <summary>Whether <paramref name="teamPosition"/> is one of <see cref="CanonicalPositions"/>.</summary>
    public static bool IsCanonicalPosition(string? teamPosition)
        => teamPosition is not null && Array.IndexOf(CanonicalPositions, teamPosition) >= 0;

    /// <summary>
    /// Whether a match is a remake rather than a game: Riot flagged the remake vote, or the
    /// match is shorter than <see cref="MinimumGameDurationSeconds"/>.
    /// </summary>
    public static bool IsRemake(int gameDurationSeconds, bool endedInEarlySurrender)
        => endedInEarlySurrender || gameDurationSeconds < MinimumGameDurationSeconds;

    /// <summary>
    /// The matches a champion read may count on <paramref name="queueId"/> and — when
    /// given — the normalised <paramref name="patch"/>: games, not remakes. The match-level
    /// half of the cohort, on its own for the reads whose champion side is deliberately
    /// wider than the mains (the composition recommender and the matchup-scoped builds,
    /// where a build is valid whoever piloted it) or narrower (one player's own games):
    /// they choose their population, but not what a game is.
    /// </summary>
    public static IQueryable<Match> Games(TrueMainDbContext db, int queueId, string? patch)
        => Games(db).Where(match => match.QueueId == queueId && (patch == null || match.Patch == patch));

    /// <summary>
    /// The champion side of a live champion read, as a query to compose: the participants
    /// of <see cref="Games(TrueMainDbContext, int, string?)"/> who are cohort members.
    /// Callers narrow it to their champion, lane and elo bands and join whatever they
    /// project; they never restate who counts.
    /// </summary>
    public static IQueryable<MatchParticipant> Members(TrueMainDbContext db, int queueId, string? patch)
        => MembersOf(db, Games(db, queueId, patch));

    /// <summary>
    /// Every match that is a game rather than a remake, whatever its queue or patch — the
    /// SQL spelling of <see cref="IsRemake"/>: neither flagged by Riot nor under the floor.
    /// </summary>
    private static IQueryable<Match> Games(TrueMainDbContext db)
        => db.Matches
            .AsNoTracking()
            .Where(match => match.GameDurationSeconds >= MinimumGameDurationSeconds
                && !match.EndedInEarlySurrender);

    /// <summary>
    /// The predicate itself, spelled once for the folds' batch load and the API's live
    /// reads. An <c>EXISTS</c> on <c>main_champion_stats</c> rather than a join: the
    /// table is unique on (platform, puuid, champion), so the two are the same set, and
    /// the semi-join lets a caller keep composing on <see cref="MatchParticipant"/>.
    /// <c>RiotAccountId IS NOT NULL</c> is implied by a main row but stated anyway — it
    /// is the filter of the partial index every champion-page read seeks
    /// (<c>IX_match_participants_champion_position_tracked</c>).
    /// </summary>
    private static IQueryable<MatchParticipant> MembersOf(TrueMainDbContext db, IQueryable<Match> games)
        => db.MatchParticipants
            .AsNoTracking()
            .Where(participant => participant.RiotAccountId != null
                && CanonicalPositions.Contains(participant.TeamPosition)
                && games.Any(match => match.Id == participant.MatchId
                    && db.MainChampionStats.Any(stat =>
                        stat.PlatformId == match.PlatformId
                        && stat.Puuid == participant.Puuid
                        && stat.ChampionId == participant.ChampionId
                        && stat.IsMain)));

    /// <summary>
    /// Loads the cohort for <paramref name="matchIds"/>. Callers test membership per
    /// participant rather than filtering their participant load, because a participant
    /// outside the cohort is still needed as somebody else's opponent or partner.
    /// </summary>
    public static async Task<ChampionCohortSnapshot> LoadAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<string> matchIds,
        CancellationToken ct)
    {
        if (matchIds.Count == 0)
        {
            return ChampionCohortSnapshot.Empty;
        }

        // The matches of the batch that are games rather than remakes. Kept separately
        // from the member keys so a fold can skip a remake as a whole, whether or not it
        // has a cohort member.
        var eligibleMatchIds = await Games(db)
            .Where(match => matchIds.Contains(match.Id))
            .Select(match => match.Id)
            .ToListAsync(ct);

        // The same predicate the API's live reads compose (Members), so the folds and the
        // panels read live cannot drift apart without this file changing.
        var members = await MembersOf(db, Games(db))
            .Where(participant => matchIds.Contains(participant.MatchId))
            .Select(participant => new ChampionCohortKey(participant.MatchId, participant.ParticipantId))
            .ToListAsync(ct);

        return new ChampionCohortSnapshot(
            [.. members],
            new HashSet<string>(eligibleMatchIds, StringComparer.Ordinal));
    }
}

/// <summary>
/// The <see cref="ChampionCohort"/> membership of one batch of matches, as a fold reads
/// it: <see cref="Includes"/> for the champion side of a row, <see cref="IncludesMatch"/>
/// for the whole-match rule (a remake contributes nothing at all, not even to a
/// population-wide normaliser).
/// </summary>
public sealed class ChampionCohortSnapshot(
    HashSet<ChampionCohortKey> members,
    HashSet<string> eligibleMatchIds)
{
    public static ChampionCohortSnapshot Empty { get; } = new([], new HashSet<string>(StringComparer.Ordinal));

    /// <summary>Number of participants in the cohort — the fold's own denominator, for logging.</summary>
    public int Count => members.Count;

    /// <summary>
    /// The member keys. Folds test membership through <see cref="Includes"/>; this is for
    /// the suite that pins the predicate itself, one clause at a time.
    /// </summary>
    public IReadOnlyCollection<ChampionCohortKey> Keys => members;

    /// <summary>Whether the match is a game the folds may count at all.</summary>
    public bool IncludesMatch(string matchId) => eligibleMatchIds.Contains(matchId);

    /// <summary>Whether this participant is the champion side of a countable row.</summary>
    public bool Includes(string matchId, int participantId)
        => members.Contains(new ChampionCohortKey(matchId, participantId));
}
