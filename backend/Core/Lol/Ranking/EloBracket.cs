namespace Core.Lol.Ranking;

/// <summary>
/// Per-tier elo bands used to scope champion builds / winrate by skill instead
/// of one blended average. A game is bucketed by the player's ranked tier
/// <em>at game time</em> (nearest <c>rank_snapshots</c> capture to the match
/// start); games with no usable snapshot fall into <see cref="Unranked"/>.
///
/// Two distinct vocabularies live here:
/// <list type="bullet">
///   <item><b>Persisted bands</b> (<see cref="Persisted"/>) — the single-tier
///   value stored on each scope row: <see cref="Iron"/> … <see cref="Diamond"/>,
///   the collapsed apex <see cref="MasterPlus"/> (Master / Grandmaster /
///   Challenger share it — LP is unbounded and the sample is thin), and
///   <see cref="Unranked"/>.</item>
///   <item><b>Cumulative thresholds</b> (<see cref="Selectable"/>) — the
///   <c>*_PLUS</c> filter values a caller sends (e.g. <c>GOLD_PLUS</c> = "Gold
///   and above"). A threshold expands at read time to the set of persisted
///   bands at or above it (see <see cref="BandsAtOrAbove"/>), so the filter is
///   just a union of the stored per-tier rows — the row count stays bounded.</item>
/// </list>
///
/// <see cref="All"/> is never stored and never expands to a band set: it is the
/// unfiltered union of every band (<see cref="Unranked"/> included) and means
/// "apply no elo clause at all".
/// </summary>
public static class EloBracket
{
    // Cumulative-threshold filter values (what a caller sends via ?elo=).
    public const string All = "ALL";
    public const string IronPlus = "IRON_PLUS";
    public const string BronzePlus = "BRONZE_PLUS";
    public const string SilverPlus = "SILVER_PLUS";
    public const string GoldPlus = "GOLD_PLUS";
    public const string PlatinumPlus = "PLATINUM_PLUS";
    public const string EmeraldPlus = "EMERALD_PLUS";
    public const string DiamondPlus = "DIAMOND_PLUS";

    // Persisted per-tier bands (what a scope row stores). MasterPlus doubles as
    // both the apex band and the "Master and above" threshold — they coincide.
    public const string Iron = "IRON";
    public const string Bronze = "BRONZE";
    public const string Silver = "SILVER";
    public const string Gold = "GOLD";
    public const string Platinum = "PLATINUM";
    public const string Emerald = "EMERALD";
    public const string Diamond = "DIAMOND";
    public const string MasterPlus = "MASTER_PLUS";
    public const string Unranked = "UNRANKED";

    // Band → ordinal weight. Higher = stronger; adjacent bands never overlap.
    // Mirrors the tier weights in <see cref="RankScore"/>. Unranked has no
    // weight (it is excluded from every cumulative "+" threshold).
    private static readonly IReadOnlyDictionary<string, int> BandWeight =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [Iron] = 0,
            [Bronze] = 1,
            [Silver] = 2,
            [Gold] = 3,
            [Platinum] = 4,
            [Emerald] = 5,
            [Diamond] = 6,
            [MasterPlus] = 7,
        };

    // Threshold → minimum band weight it admits. IRON_PLUS admits every ranked
    // band (weight >= 0) but NOT Unranked; ALL is handled separately (no clause).
    private static readonly IReadOnlyDictionary<string, int> ThresholdFloor =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [IronPlus] = 0,
            [BronzePlus] = 1,
            [SilverPlus] = 2,
            [GoldPlus] = 3,
            [PlatinumPlus] = 4,
            [EmeraldPlus] = 5,
            [DiamondPlus] = 6,
            [MasterPlus] = 7,
        };

    /// <summary>
    /// Per-tier bands the aggregator may store on a scope row (everything except
    /// the synthetic <see cref="All"/>). One scope row per band, so a filtered
    /// read is an index seek over a bounded set of bands.
    /// </summary>
    public static readonly IReadOnlyList<string> Persisted =
    [
        Iron, Bronze, Silver, Gold, Platinum, Emerald, Diamond, MasterPlus, Unranked
    ];

    /// <summary>
    /// Filter values surfaced in the picker, ordered highest rank → lowest.
    /// <see cref="All"/> leads as the default; within each tier the cumulative
    /// <c>*_PLUS</c> value ("that tier and above") precedes the exact single-tier
    /// value ("that tier only"). The apex band <see cref="MasterPlus"/> has no
    /// separate exact form (Master / GM / Challenger are one band).
    /// <see cref="Unranked"/> is never a selectable option (folded into
    /// <see cref="All"/>).
    /// </summary>
    public static readonly IReadOnlyList<string> Selectable =
    [
        All,
        MasterPlus,
        DiamondPlus, Diamond,
        EmeraldPlus, Emerald,
        PlatinumPlus, Platinum,
        GoldPlus, Gold,
        SilverPlus, Silver,
        BronzePlus, Bronze,
        IronPlus, Iron,
    ];

    /// <summary>
    /// Maps a Riot ranked tier name (e.g. <c>"DIAMOND"</c>) to the per-tier band
    /// stored on a scope row. Master / Grandmaster / Challenger collapse to
    /// <see cref="MasterPlus"/>; unknown / null / empty tiers → <see cref="Unranked"/>.
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
        "MASTER" or "GRANDMASTER" or "CHALLENGER" => MasterPlus,
        _ => Unranked
    };

    /// <summary>
    /// Expands a cumulative threshold to the persisted bands at or above it.
    /// Returns <see langword="null"/> for <see cref="All"/> / blank / non-threshold
    /// values (including exact single-tier bands) — a threshold-only helper. Use
    /// <see cref="ResolveBands"/> to resolve any filter value (exact or cumulative)
    /// to its band set.
    /// </summary>
    public static IReadOnlyList<string>? BandsAtOrAbove(string? threshold)
    {
        var normalized = Normalize(threshold);
        if (normalized is null || IsAll(normalized) || !ThresholdFloor.TryGetValue(normalized, out var floor))
        {
            return null;
        }

        return BandWeight
            .Where(entry => entry.Value >= floor)
            .OrderBy(entry => entry.Value)
            .Select(entry => entry.Key)
            .ToList();
    }

    /// <summary>
    /// Resolves any filter value to the persisted bands it selects:
    /// <list type="bullet">
    ///   <item><see cref="All"/> / blank / unknown → <see langword="null"/> ("no
    ///   elo clause", the full union incl. <see cref="Unranked"/>).</item>
    ///   <item>A cumulative <c>*_PLUS</c> threshold → every band at or above it.</item>
    ///   <item>An exact single-tier band (e.g. <c>GOLD</c>) → just that band.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<string>? ResolveBands(string? value)
    {
        var normalized = Normalize(value);
        if (normalized is null || IsAll(normalized))
        {
            return null;
        }

        // Cumulative threshold expands; an exact band selects only itself.
        return ThresholdFloor.ContainsKey(normalized) ? BandsAtOrAbove(normalized) : [normalized];
    }

    /// <summary>
    /// Normalises a caller-supplied filter value to a canonical form —
    /// <see cref="All"/>, a cumulative <c>*_PLUS</c> threshold, or an exact
    /// single-tier band (e.g. <c>GOLD</c>) — or <see langword="null"/> when
    /// blank / unrecognised (treated as "no filter" → <see cref="All"/>).
    /// </summary>
    public static string? Normalize(string? threshold)
    {
        if (string.IsNullOrWhiteSpace(threshold))
        {
            return null;
        }

        var upper = threshold.Trim().ToUpperInvariant();
        if (string.Equals(upper, All, StringComparison.Ordinal))
        {
            return All;
        }

        // A cumulative "X+" threshold, or an exact per-tier band. MasterPlus is
        // in both maps (apex band == "Master and above") and matches here once.
        return ThresholdFloor.ContainsKey(upper) || BandWeight.ContainsKey(upper) ? upper : null;
    }

    /// <summary>
    /// True when the filter means "every game" — either explicitly
    /// <see cref="All"/> or an unspecified / unrecognised value.
    /// </summary>
    public static bool IsAll(string? threshold)
        => string.IsNullOrWhiteSpace(threshold) || string.Equals(threshold, All, StringComparison.OrdinalIgnoreCase);
}
