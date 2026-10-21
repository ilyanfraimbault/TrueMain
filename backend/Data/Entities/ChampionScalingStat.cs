namespace Data.Entities;

/// <summary>
/// How a champion's mains fared by game length at a position (#1368): one row per
/// (champion, position, patch, elo band, duration bucket), counting the games of the
/// champion cohort (<c>Data.Aggregation.ChampionCohort</c>) that ended inside the bucket's
/// window — see <c>Data.Aggregation.ChampionScalingBuckets</c> for the edges. Additive,
/// with no sample floor: the scaling read folds rows to the requested patch and bands and
/// floors each bucket on the merged total, exactly as it did when it folded
/// <see cref="MatchParticipant"/> live on every request.
/// </summary>
public class ChampionScalingStat
{
    public Guid Id { get; set; }

    /// <summary>The champion side: a main of this champion, in a game that is not a remake.</summary>
    public int ChampionId { get; set; }

    /// <summary>Lane of the champion side (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY).</summary>
    public string TeamPosition { get; set; } = string.Empty;

    /// <summary>Canonical major.minor patch (e.g. "16.4").</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>
    /// Per-tier elo band of the champion side at fold time (see <c>Core.Lol.Ranking.EloBracket</c>);
    /// empty when the participant had not been stamped yet, like the matchup rows.
    /// </summary>
    public string EloBracket { get; set; } = string.Empty;

    /// <summary>Duration bucket, 0 (shortest) to <c>ChampionScalingBuckets.Count</c> − 1 (longest).</summary>
    public int Bucket { get; set; }

    public int Games { get; set; }

    /// <summary>Of <see cref="Games"/>, those the champion side won.</summary>
    public int Wins { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
