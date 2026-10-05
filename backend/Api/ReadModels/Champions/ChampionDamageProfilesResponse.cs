namespace TrueMain.ReadModels.Champions;

/// <summary>
/// How every profiled champion splits its damage to champions by type (#1905) — one payload,
/// identical for every caller, so the desktop draft builds its team bars locally instead of
/// asking once per pick.
/// </summary>
public sealed record ChampionDamageProfilesResponse
{
    /// <summary>
    /// The patch the snapshot was resolved for: the one asked for, or the newest patch carrying
    /// profiles when none was. Null when nothing has been profiled yet.
    /// </summary>
    public string? Patch { get; init; }

    /// <summary>
    /// Per-position entries, then one champion-wide entry (<c>position: null</c>) per champion
    /// for a pick at a position it has no entry for — the same resolution the item-context
    /// fold applies — and the static fallbacks.
    /// </summary>
    public IReadOnlyList<ChampionDamageProfileReadModel> Profiles { get; init; } = [];
}

/// <summary>One champion's damage profile, at one position or champion-wide.</summary>
public sealed record ChampionDamageProfileReadModel
{
    public int ChampionId { get; init; }

    /// <summary>The lane this entry answers for, or null for the champion-wide entry a lookup falls back to.</summary>
    public string? Position { get; init; }

    /// <summary>
    /// The lane the numbers were measured at: <see cref="Position"/> itself, the champion's
    /// best-covered lane on a champion-wide entry, null on a static fallback.
    /// </summary>
    public string? ProfilePosition { get; init; }

    /// <summary><c>measured</c> (folded from games) or <c>fallback</c> (a static classification, no shares).</summary>
    public string Source { get; init; } = ChampionDamageProfileSources.Measured;

    /// <summary>The patch the profile was folded on — may be older than the response's, by design of the snapshot.</summary>
    public string? Patch { get; init; }

    public int? Games { get; init; }

    /// <summary>Physical share of the damage to champions; with magic and true it sums to 1. Null on a fallback.</summary>
    public double? PhysicalShare { get; init; }

    public double? MagicShare { get; init; }

    public double? TrueShare { get; init; }

    /// <summary>Mean damage to champions per game — the weight a team bar gives this champion.</summary>
    public double? DamagePerGame { get; init; }

    /// <summary><c>physical</c>, <c>magic</c> or <c>mixed</c> on a fallback; null on a measured entry, whose shares say more.</summary>
    public string? DamageClass { get; init; }

    /// <summary>The build archetypes above the games floor, most played first.</summary>
    public IReadOnlyList<ChampionDamageBuildReadModel> Builds { get; init; } = [];

    /// <summary>Whether two builds with opposite dominant damage types each hold a meaningful share of the games.</summary>
    public bool FlexDamage { get; init; }
}

/// <summary>The damage split of the games where the champion's build leaned on one archetype.</summary>
public sealed record ChampionDamageBuildReadModel
{
    /// <summary><c>AbilityPower</c>, <c>Crit</c>, <c>ArmorPenetration</c>, <c>OnHit</c>, <c>Tank</c> or <c>None</c>.</summary>
    public string Archetype { get; init; } = string.Empty;

    public int Games { get; init; }

    /// <summary>Share of the champion's archetype-classified games at this position and patch.</summary>
    public double Share { get; init; }

    public double PhysicalShare { get; init; }

    public double MagicShare { get; init; }

    public double TrueShare { get; init; }
}

/// <summary>The values of <see cref="ChampionDamageProfileReadModel.Source"/>.</summary>
public static class ChampionDamageProfileSources
{
    public const string Measured = "measured";

    public const string Fallback = "fallback";
}
