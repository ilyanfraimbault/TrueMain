namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Side-by-side comparison of a champion's aggregates on two patches
/// (<c>GET /champions/{id}/patch-diff</c>). Powers the patch-diff section on
/// the champion detail page: it surfaces what changed for a champion between an
/// older "from" patch and a newer "to" patch — the win-rate swing, whether the
/// most popular first item moved, and whether the dominant skill order or
/// keystone changed. Both sides are read from the same
/// <c>champion_aggregate_*</c> rows the build endpoint serves, so the numbers
/// line up exactly with the per-patch build view; nothing here is synthesised.
/// The endpoint always returns 200 — a side with no data simply has a null
/// <see cref="ChampionPatchDiffSide"/>, letting the chart render its own
/// "not enough data" state instead of a 404.
/// </summary>
public sealed record ChampionPatchDiffReadModel
{
    public int ChampionId { get; init; }

    /// <summary>
    /// Position both sides are computed for. Empty string when the champion has
    /// no positioned scopes on either patch (both sides are then null too).
    /// </summary>
    public string Position { get; init; } = string.Empty;

    /// <summary>
    /// How many distinct patches have data for this (champion, position). The
    /// frontend hides the whole section below 2 — a single-patch champion can
    /// only be diffed against itself, which is a flat, meaningless comparison.
    /// </summary>
    public int AvailablePatchCount { get; init; }

    /// <summary>Older patch's slice, or null when the champion has no data there.</summary>
    public ChampionPatchDiffSide? From { get; init; }

    /// <summary>Newer patch's slice, or null when the champion has no data there.</summary>
    public ChampionPatchDiffSide? To { get; init; }

    /// <summary>
    /// Notable movements between the two sides. Null when either side is
    /// missing — a delta needs both endpoints to be meaningful.
    /// </summary>
    public ChampionPatchDiffDelta? Delta { get; init; }
}

/// <summary>
/// One patch's snapshot in the diff: the headline win rate plus the dominant
/// (most-played) build's full core — the completed item path, the whole rune
/// page and the skill order — for the resolved position. These are the exact
/// <see cref="BuildCoreReadModel"/> entries the build endpoint surfaces for the
/// same patch + position, so the diff renders side-by-side with the same
/// reusable widgets the build view uses and the two never disagree.
/// </summary>
public sealed record ChampionPatchDiffSide
{
    public string Patch { get; init; } = string.Empty;

    public int Games { get; init; }

    public int Wins { get; init; }

    /// <summary>Champion-wide win rate on this patch — <c>Wins / Games</c>, a fraction in <c>[0, 1]</c>.</summary>
    public double WinRate { get; init; }

    /// <summary>
    /// The top build's completed core item order on this patch (its
    /// <see cref="BuildCoreReadModel.ItemPath"/>). Null when the champion has no
    /// qualifying build on the patch.
    /// </summary>
    public BuildItemPathReadModel? ItemPath { get; init; }

    /// <summary>
    /// The top build's dominant rune page on this patch (primary tree +
    /// keystone, secondary tree and stat shards). Null when unavailable.
    /// </summary>
    public BuildRunePageReadModel? RunePage { get; init; }

    /// <summary>
    /// The top build's dominant skill-order sequence on this patch. Null when
    /// unavailable.
    /// </summary>
    public BuildSkillOrderReadModel? SkillOrder { get; init; }
}

/// <summary>
/// The highlighted movements between <see cref="ChampionPatchDiffReadModel.From"/>
/// and <see cref="ChampionPatchDiffReadModel.To"/>. The frontend reads these
/// flags directly to badge "new popular item", "skill order changed", etc.,
/// rather than re-deriving them from the two sides.
/// </summary>
public sealed record ChampionPatchDiffDelta
{
    /// <summary>Win-rate change, <c>To.WinRate - From.WinRate</c> (signed fraction).</summary>
    public double WinRateChange { get; init; }

    /// <summary>True when the most popular first item differs between the two patches.</summary>
    public bool FirstItemChanged { get; init; }

    /// <summary>True when the most popular primary keystone differs between the two patches.</summary>
    public bool KeystoneChanged { get; init; }

    /// <summary>True when the dominant skill order differs between the two patches.</summary>
    public bool SkillOrderChanged { get; init; }
}
