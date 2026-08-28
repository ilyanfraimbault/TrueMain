namespace Core.Lol.Ranking;

/// <summary>
/// Per-tier elo buckets used to scope champion builds / win rate by rank
/// instead of one blended average. A game is bucketed by the player's ranked
/// tier <em>at game time</em> (nearest <c>rank_snapshots</c> capture to the
/// match start). Games with no usable snapshot fall into <see cref="Unranked"/>.
///
/// One scope row is persisted per individual tier (<see cref="Ladder"/>) plus
/// <see cref="Unranked"/> — the full ladder up to <see cref="Challenger"/>,
/// with Master, Grandmaster and Challenger each their own bucket.
///
/// A read-time <em>filter</em> is one of:
/// <list type="bullet">
///   <item><see cref="All"/> — every bucket (the default, never stored).</item>
///   <item>a bare tier (e.g. <c>GOLD</c>) — that tier only.</item>
///   <item>a tier + <see cref="PlusSuffix"/> (e.g. <c>GOLD_PLUS</c>) — that tier
///   and every tier above it on the <see cref="Ladder"/>.</item>
/// </list>
/// <see cref="TryResolveFilter"/> turns a filter into the concrete set of stored
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
    public const string Grandmaster = "GRANDMASTER";
    public const string Challenger = "CHALLENGER";
    public const string Unranked = "UNRANKED";

    /// <summary>Suffix marking an "and above" filter, e.g. <c>GOLD_PLUS</c>.</summary>
    public const string PlusSuffix = "_PLUS";

    /// <summary>
    /// Cache-key token shared by every value that is not a bracket — see
    /// <see cref="ResolveToken"/> for why they get one of their own.
    /// </summary>
    public const string InvalidToken = "invalid";

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
        Master,
        Grandmaster,
        Challenger
    ];

    /// <summary>
    /// Buckets stored on scope rows: the <see cref="Ladder"/> tiers plus
    /// <see cref="Unranked"/>. The synthetic <see cref="All"/> filter is the
    /// read-time union of these and is never persisted.
    /// </summary>
    public static readonly IReadOnlyList<string> Persisted = [.. Ladder, Unranked];

    /// <summary>
    /// Maps a Riot ranked tier name (e.g. <c>"DIAMOND"</c>) to its stored
    /// bucket — one bucket per ranked tier, the apex tiers included; unknown /
    /// null / empty tiers map to <see cref="Unranked"/>.
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
        "MASTER" => Master,
        "GRANDMASTER" => Grandmaster,
        "CHALLENGER" => Challenger,
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
    /// Resolves a filter to the set of stored buckets it covers, telling apart the
    /// two cases a plain resolve collapses: <see langword="true"/> with
    /// <paramref name="bands"/> null is "every bucket" (blank / <see cref="All"/>),
    /// while <see langword="false"/> is "this is not a bracket at all".
    ///
    /// <para>
    /// They must not read alike. Answering an unrecognised value with "no
    /// restriction" serves the whole population under a rank label — <c>?elo=GOLDD</c>
    /// comes back as every bracket's games with Gold written above them — which is a
    /// fabricated number, not a lenient filter. The caller decides what to do with the
    /// rejection: reject the request, or return its documented empty state.
    /// </para>
    ///
    /// <para>
    /// A bare tier yields a single-element set; a <c>TIER_PLUS</c> filter yields that
    /// tier and everything above it on the <see cref="Ladder"/>.
    /// </para>
    /// </summary>
    public static bool TryResolveFilter(string? filter, out IReadOnlyList<string>? bands)
    {
        bands = null;

        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var value = filter.Trim().ToUpperInvariant();
        if (value == All)
        {
            return true;
        }

        var (tier, andAbove) = SplitFilter(value);
        var index = IndexInLadder(tier);
        if (index < 0)
        {
            return false;
        }

        bands = andAbove ? Ladder.Skip(index).ToList() : [Ladder[index]];
        return true;
    }

    /// <summary>
    /// The buckets a filter covers, with an unrecognised value degrading to the
    /// <em>empty</em> set rather than to every bucket: no band matches, so the read
    /// comes back empty instead of answering a rank question with the whole
    /// population. <see langword="null"/> still means "no restriction", which only a
    /// blank or <see cref="All"/> filter earns.
    /// </summary>
    /// <remarks>
    /// The safety net under the HTTP boundary, which rejects the same values with a
    /// 400 (<c>ChampionQueryParameterNormalizer</c>). Query services are reachable
    /// without MVC, and they are the layer that must never widen a scope it was not
    /// asked to widen.
    /// </remarks>
    public static IReadOnlyList<string>? ResolveFilterOrEmpty(string? filter)
        => TryResolveFilter(filter, out var bands) ? bands : [];

    /// <summary>
    /// Cache-key token for a filter: the canonical <see cref="Normalize"/> form
    /// (e.g. <c>GOLD_PLUS</c>), <c>"all"</c> for the every-bucket case (blank /
    /// <see cref="All"/>), or <see cref="InvalidToken"/> for a value that is not a
    /// bracket.
    /// </summary>
    /// <remarks>
    /// The rejected case gets its own token, and one token for all of them: it answers
    /// empty where <c>"all"</c> answers with the whole population, so sharing an entry
    /// with <c>"all"</c> would let a typo evict — or be served — the real answer. One
    /// shared token rather than the raw value, so a stream of garbage filters cannot
    /// mint an unbounded number of cache entries.
    /// </remarks>
    public static string ResolveToken(string? filter)
    {
        if (!TryResolveFilter(filter, out var bands))
        {
            return InvalidToken;
        }

        return bands is null ? "all" : Normalize(filter)!;
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
