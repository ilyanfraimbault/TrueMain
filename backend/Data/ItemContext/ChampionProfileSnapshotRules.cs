namespace Data.ItemContext;

/// <summary>
/// The one definition of how a <see cref="ChampionProfileSnapshot"/> is resolved — how far
/// back it reaches and how many games a profile needs — shared by every reader that
/// classifies a champion: the item-context fold, the next-item read and the damage-profile
/// endpoint (#1905). A bar the player sees and the axis that drives the build advice are
/// read through the same rule, so they cannot disagree.
/// </summary>
/// <remarks>
/// The ingestor's <c>ItemContextAggregation:MinProfileGames</c> and
/// <c>ProfileLookbackPatches</c> default to these; an override there must be mirrored here,
/// or the API reads would classify against a different snapshot than the fold counted.
/// </remarks>
public static class ChampionProfileSnapshotRules
{
    /// <summary>
    /// How many patches before the served one the snapshot may reach for a champion the
    /// served patch does not cover yet. Profiles fill over a patch, so on patch day every
    /// draft would otherwise be unqualifiable.
    /// </summary>
    public const int LookbackPatches = 2;

    /// <summary>
    /// Games a profile row must hold to be trusted. Measured: on production, patch 16.16
    /// held 858 <c>(champion, position)</c> lines, of which 544 clear 100 games and 459
    /// clear 200; on preprod, whose corpus is ~140x smaller, 34 of 1 002 clear 100 and 7
    /// clear 200. 100 is where both hold — the floor's real job is excluding the
    /// three-game line, not sharpening a mean.
    /// </summary>
    public const int MinGames = 100;

    /// <summary>Resolves the snapshot for <paramref name="patch"/> under these rules.</summary>
    public static Task<ChampionProfileSnapshot> LoadAsync(TrueMainDbContext db, string patch, CancellationToken ct)
        => ChampionProfileSnapshot.LoadAsync(db, patch, LookbackPatches, MinGames, ct);
}
