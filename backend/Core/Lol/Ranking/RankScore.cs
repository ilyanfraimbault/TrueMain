namespace Core.Lol.Ranking;

/// <summary>
/// Maps a Riot ranked tuple <c>(tier, division, leaguePoints)</c> to a single
/// integer score used to sort the truemains leaderboard.
///
/// Iron→Diamond have a bounded 0–99 LP range and four divisions, so each
/// division is worth <c>100</c> and each tier is worth <c>400</c>
/// (4 divisions × 100) — no overlap between adjacent tiers regardless of LP.
///
/// Master / Grandmaster / Challenger share a single super-tier (weight 7)
/// because their LP is unbounded and ladders break ties on raw LP, not on
/// the apex tier name. So a Master with 2625 LP outranks a Challenger with
/// 800 LP — that matches what u.gg / op.gg ladders show and what users
/// expect from the screenshot.
/// </summary>
public static class RankScore
{
    private const int TierMultiplier = 400;
    private const int DivisionMultiplier = 100;

    /// <summary>
    /// Returns the score for the given ranked tuple, or <c>null</c> when the
    /// tier is missing / unknown. A <c>null</c> score sorts last via
    /// <c>NULLS LAST</c> in the ORDER BY clause.
    /// </summary>
    public static int? Compute(string? tier, string? division, int leaguePoints)
    {
        if (string.IsNullOrWhiteSpace(tier))
        {
            return null;
        }

        var tierWeight = TierWeight(tier);
        if (tierWeight is null)
        {
            return null;
        }

        var divisionWeight = IsApex(tier) ? 0 : DivisionWeight(division);
        return tierWeight.Value * TierMultiplier + divisionWeight * DivisionMultiplier + leaguePoints;
    }

    private static int? TierWeight(string tier) => tier.Trim().ToUpperInvariant() switch
    {
        "IRON" => 0,
        "BRONZE" => 1,
        "SILVER" => 2,
        "GOLD" => 3,
        "PLATINUM" => 4,
        "EMERALD" => 5,
        "DIAMOND" => 6,
        "MASTER" or "GRANDMASTER" or "CHALLENGER" => 7,
        _ => null,
    };

    private static int DivisionWeight(string? division) => division?.Trim().ToUpperInvariant() switch
    {
        "I" => 3,
        "II" => 2,
        "III" => 1,
        "IV" => 0,
        _ => 0,
    };

    private static bool IsApex(string tier) => tier.Trim().ToUpperInvariant()
        is "MASTER" or "GRANDMASTER" or "CHALLENGER";
}
