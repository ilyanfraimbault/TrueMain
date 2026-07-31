namespace TrueMain.ReadModels.Champions;

/// <summary>
/// One row of the champion directory (<c>GET /champions</c>) — one entry per
/// <c>(champion, position)</c> pair, computed against a single patch (the
/// requested one, or the global latest if unspecified). A champion played in
/// multiple lanes therefore surfaces once per lane.
/// </summary>
public sealed record ChampionSummaryReadModel
{
    public int ChampionId { get; init; }

    public int Games { get; init; }

    public int Wins { get; init; }

    public double WinRate { get; init; }

    /// <summary>
    /// Share of TrueMain games on this position taken by this champion —
    /// a main-population pickrate, derived from the same
    /// <c>champion_aggregate_scopes</c> rows as <see cref="Games"/> /
    /// <see cref="Wins"/> / <see cref="LanePlayRate"/>. Not a meta-wide
    /// ratio: the denominator is the sum of scoped games for this position
    /// on the patch, not every observed game on the patch.
    /// </summary>
    public double PickRate { get; init; }

    /// <summary>
    /// Share of this champion's own games played on this position — i.e. the
    /// champion's role distribution. A champion played 60% mid / 40% top has
    /// <c>LanePlayRate = 0.6</c> on its mid row and <c>0.4</c> on its top row.
    /// </summary>
    public double LanePlayRate { get; init; }

    public int TrueMainCount { get; init; }

    /// <summary>
    /// Share of the observed matches on this patch that banned the champion (#920).
    /// Lane-independent — a ban happens before roles exist — so every row of a
    /// champion carries the same value.
    ///
    /// <para>
    /// <see langword="null"/> means "not observed", not "never banned": bans are
    /// read from <c>champion_ban_stats</c>, which only has rows for matches ingested
    /// since the feature shipped, and Riot payloads are not retained so older patches
    /// could not be backfilled. Clients must render the gap rather than a 0%.
    /// </para>
    ///
    /// <para>
    /// Its denominator is every observed match, unlike <see cref="PickRate"/>, whose
    /// denominator is tracked mains' games at this lane. The two are therefore not
    /// addable — which is why no pick+ban "presence" figure is offered.
    /// </para>
    /// </summary>
    public double? BanRate { get; init; }

    /// <summary>
    /// OPGG-style performance tier (<c>S</c> / <c>A</c> / <c>B</c> / <c>C</c> /
    /// <c>D</c>) for this <c>(champion, position)</c> on the active patch.
    /// Computed by <see cref="Services.Champions.ChampionTierCalculator"/> from
    /// a pick-rate + ban-rate + win-rate blend (#971 — presence-first, weighted
    /// toward pick and ban rate over a sample-shrunk win rate), then bucketed by
    /// patch-wide percentile — so it is always relative to the current patch's
    /// field, not an absolute winrate cutoff. Defaults to <see cref="string.Empty"/>
    /// until assigned.
    /// </summary>
    public string Tier { get; init; } = string.Empty;

    /// <summary>
    /// The blended score (#971) that placed this row in <see cref="Tier"/>,
    /// scoped to the whole patch-wide field this row was tiered against. Not
    /// comparable across calls scoped differently — in particular, not
    /// comparable to the per-position score <c>GET /champions/tierlist</c>
    /// computes for the same <c>(champion, position)</c> row, since that
    /// endpoint re-tiers within each lane independently. Useful for ordering
    /// this endpoint's own rows (e.g. the homepage teaser) by tier strength
    /// without re-deriving an ad-hoc sort.
    /// </summary>
    public double TierScore { get; init; }

    public string Position { get; init; } = string.Empty;

    public string PatchVersion { get; init; } = string.Empty;

    public DateTime LastUpdatedAtUtc { get; init; }

    /// <summary>
    /// Most-played build for this <c>(champion, position)</c> on the active
    /// patch — keystone, secondary tree, item sequence. Null when no
    /// pattern rows exist (rare; e.g. positions with summary games but no
    /// observed builds yet).
    /// </summary>
    public TopBuildReadModel? TopBuild { get; init; }
}
