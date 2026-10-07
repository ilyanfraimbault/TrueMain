using System.Diagnostics.CodeAnalysis;

namespace Core.Lol.Ranking;

/// <summary>
/// The text form of <see cref="RankTier"/> and <see cref="RankDivision"/>: Riot's own
/// spelling (<c>"DIAMOND"</c>, <c>"II"</c>), which is what the API serves and what the
/// database stores. Parsing is the boundary where a Riot string becomes a typed rank.
/// </summary>
public static class RankTiers
{
    /// <summary>Riot's upper-case tier name, e.g. <c>"DIAMOND"</c>.</summary>
    public static string ToRiotName(this RankTier tier) => tier switch
    {
        RankTier.Iron => EloBracket.Iron,
        RankTier.Bronze => EloBracket.Bronze,
        RankTier.Silver => EloBracket.Silver,
        RankTier.Gold => EloBracket.Gold,
        RankTier.Platinum => EloBracket.Platinum,
        RankTier.Emerald => EloBracket.Emerald,
        RankTier.Diamond => EloBracket.Diamond,
        RankTier.Master => EloBracket.Master,
        RankTier.Grandmaster => EloBracket.Grandmaster,
        RankTier.Challenger => EloBracket.Challenger,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown rank tier."),
    };

    /// <summary>Riot's Roman numeral, e.g. <c>"II"</c>.</summary>
    public static string ToRiotName(this RankDivision division) => division switch
    {
        RankDivision.I => "I",
        RankDivision.II => "II",
        RankDivision.III => "III",
        RankDivision.IV => "IV",
        _ => throw new ArgumentOutOfRangeException(nameof(division), division, "Unknown rank division."),
    };

    /// <summary>
    /// Parses a Riot tier name, case- and whitespace-insensitively. <c>UNRANKED</c>, blank
    /// and unknown values are not tiers and return <see langword="false"/>.
    /// </summary>
    public static bool TryParseTier([NotNullWhen(true)] string? value, out RankTier tier)
    {
        RankTier? parsed = value?.Trim().ToUpperInvariant() switch
        {
            "IRON" => RankTier.Iron,
            "BRONZE" => RankTier.Bronze,
            "SILVER" => RankTier.Silver,
            "GOLD" => RankTier.Gold,
            "PLATINUM" => RankTier.Platinum,
            "EMERALD" => RankTier.Emerald,
            "DIAMOND" => RankTier.Diamond,
            "MASTER" => RankTier.Master,
            "GRANDMASTER" => RankTier.Grandmaster,
            "CHALLENGER" => RankTier.Challenger,
            _ => null,
        };

        tier = parsed.GetValueOrDefault();
        return parsed.HasValue;
    }

    /// <summary>Parses a Riot division numeral (<c>I</c>…<c>IV</c>), case- and whitespace-insensitively.</summary>
    public static bool TryParseDivision([NotNullWhen(true)] string? value, out RankDivision division)
    {
        RankDivision? parsed = value?.Trim().ToUpperInvariant() switch
        {
            "I" => RankDivision.I,
            "II" => RankDivision.II,
            "III" => RankDivision.III,
            "IV" => RankDivision.IV,
            _ => null,
        };

        division = parsed.GetValueOrDefault();
        return parsed.HasValue;
    }

    /// <summary>
    /// <see cref="TryParseTier"/> for a value that must be a tier — a stored column, a
    /// configured ladder. Throws <see cref="FormatException"/> otherwise.
    /// </summary>
    public static RankTier ParseTier(string value)
        => TryParseTier(value, out var tier)
            ? tier
            : throw new FormatException($"'{value}' is not a Riot rank tier.");

    /// <summary>
    /// <see cref="TryParseDivision"/> for a value that must be a division. Throws
    /// <see cref="FormatException"/> otherwise.
    /// </summary>
    public static RankDivision ParseDivision(string value)
        => TryParseDivision(value, out var division)
            ? division
            : throw new FormatException($"'{value}' is not a Riot rank division.");
}
