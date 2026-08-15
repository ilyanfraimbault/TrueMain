using Microsoft.EntityFrameworkCore;

namespace Data.Aggregation;

/// <summary>
/// Identifies one participant row inside a batch of matches. Composite because
/// <c>ParticipantId</c> is Riot's 1-10 slot number and is only unique within a match.
/// </summary>
public readonly record struct MatchupCohortKey(string MatchId, int ParticipantId);

/// <summary>
/// The champion-side cohort of the matchup folds: which participants of a batch of
/// matches may contribute a <c>champion_matchup_stats</c> row for the champion they
/// played. Lives in <c>Data</c> because three consumers must agree on it —
/// <c>ChampionMatchupLeadAggregationProcess</c> (games / wins),
/// <c>ChampionLaneOutcomeAggregationProcess</c> (the lane counters folded onto the
/// same rows) and, by construction, the champion aggregates the panel is read beside.
///
/// <para>
/// <b>Why this exists at all.</b> The two folds used to gate on
/// <c>RiotAccountId != null</c> — "we know this account" — while the champion
/// aggregates that feed the page header, the tier list, the trend and the builds gate
/// on <c>main_champion_stats.IsMain</c> — "this player is a main of this champion",
/// the site's entire premise. Measured on production, that put 14 576 games behind
/// the matchups panel and 4 605 behind the header immediately above it, on the same
/// champion, lane and patch: a factor of 3.2 between two numbers a reader compares by
/// eye. The comment on the read side asserted the two cohorts matched, which had
/// never been true.
/// </para>
///
/// <para>
/// <b>Champion side only.</b> The opponent is whoever was in that lane, main or not,
/// tracked or not — narrowing it would measure "how this champion's mains do against
/// that champion's mains", which is a different and much thinner question than the
/// one the panel asks.
/// </para>
///
/// <para>
/// <b>Not retroactive.</b> Both folds are additive and frozen patches can never be
/// recomputed (#466), so tightening this gate does not correct rows already written;
/// the migration that shipped it wipes <c>champion_matchup_stats</c> and re-folds the
/// retained window instead. Loosening it later would need the same treatment.
/// </para>
/// </summary>
public static class MatchupCohort
{
    /// <summary>
    /// Loads the cohort keys for <paramref name="matchIds"/>: every participant that
    /// is both linked to a known account and a main of the champion it is playing in
    /// that row. Callers test membership per participant; a participant absent from
    /// the set is still needed as somebody else's opponent, which is why this returns
    /// a set to filter against rather than filtering the participant load itself.
    /// </summary>
    public static async Task<HashSet<MatchupCohortKey>> LoadAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<string> matchIds,
        CancellationToken ct)
    {
        if (matchIds.Count == 0)
        {
            return [];
        }

        // Joined on (platform, puuid, champion) — the same key and the same IsMain
        // predicate ChampionPatternSourceRowReader uses, so the two cohorts cannot
        // drift apart without this line changing. IsActive is deliberately not tested:
        // it retires a main from *future ingestion* (#900), and applying it here would
        // silently drop already-folded history the moment a player stopped playing.
        var keys = await (
            from participant in db.MatchParticipants.AsNoTracking()
            join match in db.Matches.AsNoTracking()
                on participant.MatchId equals match.Id
            join stat in db.MainChampionStats.AsNoTracking()
                on new { match.PlatformId, participant.Puuid, participant.ChampionId }
                equals new { stat.PlatformId, stat.Puuid, stat.ChampionId }
            where matchIds.Contains(participant.MatchId)
                && participant.RiotAccountId != null
                && stat.IsMain
            select new MatchupCohortKey(participant.MatchId, participant.ParticipantId))
            .ToListAsync(ct);

        return [.. keys];
    }
}
