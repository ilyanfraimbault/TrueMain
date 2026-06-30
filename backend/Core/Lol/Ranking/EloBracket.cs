namespace Core.Lol.Ranking;

/// <summary>
/// Coarse elo buckets used to scope champion builds / winrate by skill band
/// instead of one blended average. A game is bucketed by the player's ranked
/// tier <em>at game time</em> (nearest <c>rank_snapshots</c> capture to the
/// match start). Games with no usable snapshot fall into
/// <see cref="Unranked"/>.
///
/// Buckets (deliberately broad so high brackets keep a usable sample):
/// <list type="bullet">
///   <item><see cref="IronGold"/> — Iron, Bronze, Silver, Gold.</item>
///   <item><see cref="PlatinumEmerald"/> — Platinum, Emerald.</item>
///   <item><see cref="DiamondPlus"/> — Diamond.</item>
///   <item><see cref="MasterPlus"/> — Master, Grandmaster, Challenger.</item>
///   <item><see cref="Unranked"/> — no snapshot / unknown tier.</item>
/// </list>
///
/// <see cref="All"/> is never persisted on a scope row — it is the read-time
/// union of every other bracket, so adding a bracket dimension never doubles
/// the stored row count.
/// </summary>
public static class EloBracket
{
    public const string All = "ALL";
    public const string IronGold = "IRON_GOLD";
    public const string PlatinumEmerald = "PLATINUM_EMERALD";
    public const string DiamondPlus = "DIAMOND_PLUS";
    public const string MasterPlus = "MASTER_PLUS";
    public const string Unranked = "UNRANKED";

    /// <summary>
    /// Brackets that are precomputed and stored on scope rows (everything
    /// except the synthetic <see cref="All"/> union). The aggregator emits one
    /// scope row per persisted bracket so a per-bracket read is a single index
    /// seek.
    /// </summary>
    public static readonly IReadOnlyList<string> Persisted =
    [
        IronGold,
        PlatinumEmerald,
        DiamondPlus,
        MasterPlus,
        Unranked
    ];

    /// <summary>
    /// Brackets surfaced as selectable filters on the champion page, ordered
    /// from broadest to narrowest. <see cref="All"/> leads as the default;
    /// <see cref="Unranked"/> is intentionally omitted from the picker (it is
    /// folded into <see cref="All"/> but not a band a user would pick).
    /// </summary>
    public static readonly IReadOnlyList<string> Selectable =
    [
        All,
        IronGold,
        PlatinumEmerald,
        DiamondPlus,
        MasterPlus
    ];

    /// <summary>
    /// Maps a Riot ranked tier name (e.g. <c>"DIAMOND"</c>) to its bracket.
    /// Unknown / null / empty tiers map to <see cref="Unranked"/>.
    /// </summary>
    public static string FromTier(string? tier) => tier?.Trim().ToUpperInvariant() switch
    {
        "IRON" or "BRONZE" or "SILVER" or "GOLD" => IronGold,
        "PLATINUM" or "EMERALD" => PlatinumEmerald,
        "DIAMOND" => DiamondPlus,
        "MASTER" or "GRANDMASTER" or "CHALLENGER" => MasterPlus,
        _ => Unranked
    };

    /// <summary>
    /// Normalises a caller-supplied bracket filter to a canonical persisted
    /// bracket, <see cref="All"/>, or <see langword="null"/> when the value is
    /// blank / unrecognised (treated as "no filter" → <see cref="All"/>).
    /// </summary>
    public static string? Normalize(string? bracket)
    {
        if (string.IsNullOrWhiteSpace(bracket))
        {
            return null;
        }

        return bracket.Trim().ToUpperInvariant() switch
        {
            All => All,
            IronGold => IronGold,
            PlatinumEmerald => PlatinumEmerald,
            DiamondPlus => DiamondPlus,
            MasterPlus => MasterPlus,
            Unranked => Unranked,
            _ => null
        };
    }

    /// <summary>
    /// True when the bracket means "every game" — either explicitly
    /// <see cref="All"/> or an unspecified filter.
    /// </summary>
    public static bool IsAll(string? bracket)
        => string.IsNullOrWhiteSpace(bracket) || string.Equals(bracket, All, StringComparison.OrdinalIgnoreCase);
}
