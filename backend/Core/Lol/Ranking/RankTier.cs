namespace Core.Lol.Ranking;

/// <summary>
/// A Riot ranked tier, in ladder order. The numeric values are explicit so the order is
/// part of the contract, but they are never persisted: every column that stores a tier
/// keeps Riot's upper-case name (<c>"DIAMOND"</c>) — see <see cref="RankTiers"/> for the
/// text form and <c>Data.Configurations.RankTierConverter</c> for the mapping.
/// </summary>
public enum RankTier
{
    Iron = 0,
    Bronze = 1,
    Silver = 2,
    Gold = 3,
    Platinum = 4,
    Emerald = 5,
    Diamond = 6,
    Master = 7,
    Grandmaster = 8,
    Challenger = 9,
}

/// <summary>
/// A division within a <see cref="RankTier"/>. The apex tiers have a single division,
/// which Riot reports as <see cref="I"/>. Stored as its Roman numeral, the member name.
/// </summary>
/// <remarks>
/// <see cref="I"/> is <c>0</c> on purpose: the default value must be a real division, because
/// EF converts the CLR default through the value converter (the compiled model does it at
/// generation time), and <c>I</c> is also what an apex tier's division is.
/// </remarks>
public enum RankDivision
{
    I = 0,
    II = 1,
    III = 2,
    IV = 3,
}
