namespace Core.Lol.Ranking;

/// <summary>
/// Per-tier elo buckets used to scope champion builds / win rate by rank
/// instead of one blended average. A game is bucketed by the player's ranked
/// tier <em>at game time</em> (nearest <c>rank_snapshots</c> capture to the
/// match start). Games with no usable snapshot fall into <see cref="Unranked"/>.
///
/// One scope row is persisted per individual tier (<see cref="Ladder"/>) plus
/// <see cref="Unranked"/>. The apex tiers (Grandmaster, Challenger) fold into
/// <see cref="Master"/> — they share an emblem and are too thin to split.
///
/// A read-time <em>filter</em> is one of:
/// <list type="bullet">
///   <item><see cref="All"/> — every bucket (the default, never stored).</item>
///   <item>a bare tier (e.g. <c>GOLD</c>) — that tier only.</item>
///   <item>a tier + <see cref="PlusSuffix"/> (e.g. <c>GOLD_PLUS</c>) — that tier
///   and every tier above it on the <see cref="Ladder"/>.</item>
/// </list>
/// <see cref="ResolveFilter"/> turns a filter into the concrete set of stored
/// buckets to read, so a "rank and above" filter is a single <c>IN</c> query.
/// </summary>
public static class EloBracket
{
    public const string All = "ALL";
    public const string Iron = "IRON";
    public const string Bronze = "BRONZE";
    public const string Silver = "SILVER";
    public const string Gold = "GOLD";
    public const string Platinum = "PLATINUM";
    public const string Emerald = "EMERALD";
    public const string Diamond = "DIAMOND";
    public const string Master = "MASTER";
    public const string Unranked = "UNRANKED";

    /// <summary>Suffix marking an "and above" filter, e.g. <c>GOLD_PLUS</c>.</summary>
    public const string PlusSuffix = "_PLUS";

    /// <summary>
    /// The ranked tiers in ascending order. Position defines "and above": a
    /// <c>TIER_PLUS</c> filter reads this tier and everything after it.
    /// </summary>
    public static readonly IReadOnlyList<string> Ladder =
    [
        Iron,
        Bronze,
        Silver,
        Gold,
        Platinum,
        Emerald,
        Diamond,
        Master
    ];

    /// <summary>
    /// Buckets stored on scope rows: the <see cref="Ladder"/> tiers plus
    /// <see cref="Unranked"/>. The synthetic <see cref="All"/> filter is the
    /// read-time union of these and is never persisted.
    /// </summary>
    public static readonly IReadOnlyList<string> Persisted = [.. Ladder, Unranked];

    /// <summary>
    /// Maps a Riot ranked tier name (e.g. <c>"DIAMOND"</c>) to its stored
    /// bucket. Master / Grandmaster / Challenger all fold into
    /// <see cref="Master"/>; unknown / null / empty tiers map to
    /// <see cref="Unranked"/>.
    /// </summary>
    public static string FromTier(string? tier) => tier?.Trim().ToUpperInvariant() switch
    {
        "IRON" => Iron,
        "BRONZE" => Bronze,
        "SILVER" => Silver,
        "GOLD" => Gold,
        "PLATINUM" => Platinum,
        "EMERALD" => Emerald,
        "DIAMOND" => Diamond,
        "MASTER" or "GRANDMASTER" or "CHALLENGER" => Master,
        _ => Unranked
    };

    /// <summary>
    /// Canonicalises a caller-supplied filter to <see cref="All"/>, a bare tier,
    /// or a <c>TIER_PLUS</c> form; returns <see langword="null"/> when the value
    /// is blank / unrecognised (treated as "no filter" → <see cref="All"/>).
    /// </summary>
    public static string? Normalize(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        var value = filter.Trim().ToUpperInvariant();
        if (value == All)
        {
            return All;
        }

        var (tier, andAbove) = SplitFilter(value);
        if (IndexInLadder(tier) < 0)
        {
            return null;
        }

        return andAbove ? tier + PlusSuffix : tier;
    }

    /// <summary>
    /// Resolves a filter to the set of stored buckets it covers, or
    /// <see langword="null"/> for the <see cref="All"/> / blank / unrecognised
    /// case (no filter — the caller then spans every bucket). A bare tier
    /// yields a single-element set; a <c>TIER_PLUS</c> filter yields that tier
    /// and everything above it on the <see cref="Ladder"/>.
    /// </summary>
    public static IReadOnlyList<string>? ResolveFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        var value = filter.Trim().ToUpperInvariant();
        if (value == All)
        {
            return null;
        }

        var (tier, andAbove) = SplitFilter(value);
        var index = IndexInLadder(tier);
        if (index < 0)
        {
            return null;
        }

        return andAbove ? Ladder.Skip(index).ToList() : [Ladder[index]];
    }

    private static (string Tier, bool AndAbove) SplitFilter(string value)
        => value.EndsWith(PlusSuffix, StringComparison.Ordinal)
            ? (value[..^PlusSuffix.Length], true)
            : (value, false);

    private static int IndexInLadder(string tier)
    {
        for (var i = 0; i < Ladder.Count; i++)
        {
            if (string.Equals(Ladder[i], tier, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
