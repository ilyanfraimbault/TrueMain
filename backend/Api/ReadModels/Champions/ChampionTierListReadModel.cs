namespace TrueMain.ReadModels.Champions;

/// <summary>
/// The champion meta / tier-list view (<c>GET /champions/tierlist</c>) for a
/// single patch, optionally narrowed to one position. Champions are grouped
/// into S / A / B / C / D tiers by a winRate + pickRate blend, with the
/// tiering computed <b>per position</b> — an S-tier mid laner is S among mid
/// laners, not against the whole patch — so the page reads as a meta ranking
/// for the role the user is filtering on.
///
/// <para>
/// Every metric is derived from the same <c>champion_aggregate_scopes</c> rows
/// the champion directory reads (see <see cref="ChampionSummaryReadModel"/>);
/// nothing here is synthesised. The tier thresholds are documented on
/// <see cref="Services.Champions.ChampionTierCalculator"/>.
/// </para>
/// </summary>
public sealed record ChampionTierListReadModel
{
    /// <summary>Canonical <c>major.minor</c> patch the tiers were computed for.</summary>
    public string PatchVersion { get; init; } = string.Empty;

    /// <summary>
    /// The position the list is scoped to (<c>TOP</c> / <c>JUNGLE</c> /
    /// <c>MIDDLE</c> / <c>BOTTOM</c> / <c>UTILITY</c>), or <c>null</c> when the
    /// caller asked for every position at once. When null, tiering is still
    /// computed independently within each position.
    /// </summary>
    public string? Position { get; init; }

    /// <summary>
    /// Tier groups in descending strength (S first), each carrying its ranked
    /// champion rows. Empty tiers are omitted, so a sparse patch may surface
    /// fewer than five groups.
    /// </summary>
    public IReadOnlyList<ChampionTierGroupReadModel> Tiers { get; init; } = [];
}

/// <summary>One S/A/B/C/D bucket and the champion rows that fell into it.</summary>
public sealed record ChampionTierGroupReadModel
{
    /// <summary>Tier letter: <c>S</c> / <c>A</c> / <c>B</c> / <c>C</c> / <c>D</c>.</summary>
    public string Tier { get; init; } = string.Empty;

    /// <summary>
    /// Rows in this tier, ordered strongest-first by the same winRate + pickRate
    /// score that placed them in the bucket.
    /// </summary>
    public IReadOnlyList<ChampionTierEntryReadModel> Entries { get; init; } = [];
}

/// <summary>
/// A single <c>(champion, position)</c> row of the tier list. Mirrors the
/// directory's metrics so the page can render winrate / pickrate and link
/// straight to the champion page.
/// </summary>
public sealed record ChampionTierEntryReadModel
{
    public int ChampionId { get; init; }

    public string Position { get; init; } = string.Empty;

    public int Games { get; init; }

    public double WinRate { get; init; }

    /// <summary>
    /// Share of TrueMain games on this position taken by this champion — the
    /// same main-population pickrate as <see cref="ChampionSummaryReadModel.PickRate"/>.
    /// </summary>
    public double PickRate { get; init; }
}
