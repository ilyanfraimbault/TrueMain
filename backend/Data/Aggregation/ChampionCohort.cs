using Microsoft.EntityFrameworkCore;

namespace Data.Aggregation;

/// <summary>
/// Identifies one participant row inside a batch of matches. Composite because
/// <c>ParticipantId</c> is Riot's 1-10 slot number and is only unique within a match.
/// </summary>
public readonly record struct ChampionCohortKey(string MatchId, int ParticipantId);

/// <summary>
/// The one definition of "this participant counts for the champion they played" that
/// every champion-page fold composes: <b>the account is tracked, the match is not a
/// remake, the position is canonical, and <c>main_champion_stats.IsMain</c> says this
/// player is a main of that champion</b>.
///
/// <para>
/// <b>Why it lives in <c>Data</c> and why it is shared.</b> Four folds write the panels
/// stacked on one champion page — <c>ChampionMatchupLeadAggregationProcess</c> and
/// <c>ChampionLaneOutcomeAggregationProcess</c> (the matchups panel),
/// <c>ChampionSynergyAggregationProcess</c> (synergies) and
/// <c>ChampionPowerspikeAggregationProcess</c> (power spikes) — beside a header read
/// from <c>champion_aggregate_scopes</c>. Whenever one of them restates the cohort in
/// its own words the numbers drift apart while still looking comparable: #1087 measured
/// 14 576 games behind the matchups panel against 4 605 behind the header immediately
/// above it, on the same champion, lane and patch — a factor of 3.2 — because the folds
/// gated on <c>RiotAccountId != null</c> ("an account we know") while the aggregate
/// gated on <c>IsMain</c> ("a main of this champion", the site's premise). #1087 fixed
/// the two matchup folds; the synergy and powerspike folds carried the same defect
/// until #1365 pointed them here too.
/// </para>
///
/// <para>
/// <b>The queried side only.</b> The opponent of a matchup, the partner of a synergy
/// pairing and the lane opponent a spike is measured against are whoever was in that
/// game, main or not, tracked or not. Narrowing them would measure "how this champion's
/// mains do against/with that champion's mains", a different and much thinner question —
/// and for synergy it would break the maths outright, since the expected value is built
/// from a partner side drawn from the general population (#922).
/// </para>
///
/// <para>
/// <b>Mains, not the widened population.</b> The aggregate behind the header carries
/// both populations since #1346 and its reads choose, defaulting to truemains; these
/// four tables carry no such dimension, so they answer for mains only and the matchups
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
/// <b>Remakes.</b> Riot's own <c>gameEndedInEarlySurrender</c> is not stored on
/// <c>match_participants</c>, so the rule is a duration floor —
/// <see cref="MinimumGameDurationSeconds"/> — held here rather than restated per fold.
/// On production 4 762 stored matches (1.7%) sit under it. Should the Riot flag ever be
/// persisted, this is the single place that changes.
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
    /// count as a game, each one deciding for itself whether to. Riot's
    /// <c>gameEndedInEarlySurrender</c> would be the exact signal but is not stored, and
    /// the vote cannot open before 3 minutes nor the game end much past 4, so the floor
    /// separates the two populations cleanly without a schema change.
    /// </summary>
    public const int MinimumGameDurationSeconds = 300;

    /// <summary>
    /// The five canonical lane positions. A participant with an empty or garbage
    /// <c>TeamPosition</c> cannot be placed in a composition, a matchup or a lane, so it
    /// is not part of any champion cohort — on either side of a pairing.
    /// </summary>
    public static readonly string[] CanonicalPositions = ["TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY"];

    /// <summary>Whether <paramref name="teamPosition"/> is one of <see cref="CanonicalPositions"/>.</summary>
    public static bool IsCanonicalPosition(string? teamPosition)
        => teamPosition is not null && Array.IndexOf(CanonicalPositions, teamPosition) >= 0;

    /// <summary>Whether a match of this length is a remake rather than a game.</summary>
    public static bool IsRemake(int gameDurationSeconds)
        => gameDurationSeconds < MinimumGameDurationSeconds;

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
        // from the member keys because the powerspike fold's normaliser is accumulated
        // over every lane pair of a match, tracked or not, and still must not be fed a
        // remake.
        var eligibleMatchIds = await db.Matches
            .AsNoTracking()
            .Where(match => matchIds.Contains(match.Id)
                && match.GameDurationSeconds >= MinimumGameDurationSeconds)
            .Select(match => match.Id)
            .ToListAsync(ct);

        // Joined on (platform, puuid, champion) — the same key and the same IsMain
        // predicate the champion aggregate's source-row reader uses, so the panels
        // cannot drift apart without this line changing.
        var members = await (
            from participant in db.MatchParticipants.AsNoTracking()
            join match in db.Matches.AsNoTracking()
                on participant.MatchId equals match.Id
            join stat in db.MainChampionStats.AsNoTracking()
                on new { match.PlatformId, participant.Puuid, participant.ChampionId }
                equals new { stat.PlatformId, stat.Puuid, stat.ChampionId }
            where matchIds.Contains(participant.MatchId)
                && participant.RiotAccountId != null
                && stat.IsMain
                && match.GameDurationSeconds >= MinimumGameDurationSeconds
                && CanonicalPositions.Contains(participant.TeamPosition)
            select new ChampionCohortKey(participant.MatchId, participant.ParticipantId))
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
