namespace Core.Lol.Map;

/// <summary>
/// Summoner's Rift jungle camp coordinates and a <c>(x, y) → nearest camp</c>
/// helper (issue #535; the piece #538 deferred for lack of an authoritative
/// source). Coordinates are the documented community/HextechDocs centroids of
/// each camp, expressed on the same timeline coordinate space as
/// <see cref="LolMap"/> (x ∈ [-120, 14870], y ∈ [-120, 14980]). Blue side is the
/// bottom-left half of the map; red side is the top-right half (mirror image).
/// Scuttle crabs sit on the river anti-diagonal and are shared, so they are not
/// side-qualified.
///
/// The nearest-camp mapping is the standard accepted method for reconstructing a
/// jungler's first clear from per-minute position frames (Doran's Lab / League of
/// Graphs): a sampled position is assigned to its <b>closest</b> centroid, which
/// reliably identifies the camp the jungler is on during the first clear (movement
/// ~1 camp/min). Adjacent camps are only ~1200–1900 units apart, so the mapping
/// relies on closest-centroid-wins rather than on wide gaps between camps.
/// </summary>
public static class JungleCamps
{
    /// <summary>
    /// A jungler standing further than this (units) from every camp centroid is
    /// considered to be between camps / recalling / ganking — not on any camp. This
    /// only gates on-a-camp vs. off-any-camp; which camp is chosen is always the
    /// closest centroid, so the radius being wider than the ~1200-unit gap between
    /// adjacent camps does not cause mis-assignment (closest still wins).
    /// </summary>
    public const double MaxAssignmentDistance = 2200.0;

    /// <summary>
    /// Centroid (x, y) of every jungle camp. Coordinates are approximate documented
    /// community (HextechDocs) values; the nearest-camp mapping only needs them
    /// accurate enough to be each camp's closest centroid, which holds with wide
    /// margins. The JungleCampsTests suite pins these exact values, so it is the
    /// anchor to update (and to re-derive the source against) if Riot ever reshapes
    /// the jungle geometry.
    /// </summary>
    public static readonly IReadOnlyDictionary<JungleCamp, (int X, int Y)> Coordinates =
        new Dictionary<JungleCamp, (int X, int Y)>
        {
            // Blue side (bottom-left half).
            [JungleCamp.BlueGromp] = (2150, 8420),
            [JungleCamp.BlueBlueBuff] = (3820, 7920),
            [JungleCamp.BlueWolves] = (3650, 6500),
            [JungleCamp.BlueRaptors] = (6900, 5500),
            [JungleCamp.BlueRedBuff] = (7770, 3800),
            [JungleCamp.BlueKrugs] = (8400, 2750),

            // Red side (top-right half) — point mirror of blue around the map centre.
            [JungleCamp.RedGromp] = (12600, 6560),
            [JungleCamp.RedBlueBuff] = (10930, 7060),
            [JungleCamp.RedWolves] = (11100, 8480),
            [JungleCamp.RedRaptors] = (7850, 9480),
            [JungleCamp.RedRedBuff] = (6980, 11180),
            [JungleCamp.RedKrugs] = (6350, 12230),

            // Scuttle spots on the river (shared, not side-qualified).
            [JungleCamp.ScuttleTop] = (4400, 9700),
            [JungleCamp.ScuttleBottom] = (10100, 5300)
        };

    /// <summary>
    /// The blue-side first-clear camp set (the six non-scuttle blue camps). Used by
    /// the clear reconstruction to know when a full clear has been reached.
    /// </summary>
    public static readonly IReadOnlyList<JungleCamp> BlueSideCamps =
    [
        JungleCamp.BlueGromp,
        JungleCamp.BlueBlueBuff,
        JungleCamp.BlueWolves,
        JungleCamp.BlueRaptors,
        JungleCamp.BlueRedBuff,
        JungleCamp.BlueKrugs
    ];

    /// <summary>
    /// The red-side first-clear camp set (the six non-scuttle red camps).
    /// </summary>
    public static readonly IReadOnlyList<JungleCamp> RedSideCamps =
    [
        JungleCamp.RedGromp,
        JungleCamp.RedBlueBuff,
        JungleCamp.RedWolves,
        JungleCamp.RedRaptors,
        JungleCamp.RedRedBuff,
        JungleCamp.RedKrugs
    ];

    /// <summary>
    /// Maps a timeline (x, y) position to the nearest jungle camp centroid, or
    /// <see cref="JungleCamp.Unknown"/> when the position is further than
    /// <see cref="MaxAssignmentDistance"/> from every camp (between camps, recalling,
    /// ganking a lane).
    /// </summary>
    public static JungleCamp NearestCamp(int x, int y)
    {
        var nearest = JungleCamp.Unknown;
        var nearestSquared = double.MaxValue;

        foreach (var (camp, coordinate) in Coordinates)
        {
            double dx = x - coordinate.X;
            double dy = y - coordinate.Y;
            var distanceSquared = (dx * dx) + (dy * dy);
            if (distanceSquared < nearestSquared)
            {
                nearestSquared = distanceSquared;
                nearest = camp;
            }
        }

        return nearestSquared <= MaxAssignmentDistance * MaxAssignmentDistance
            ? nearest
            : JungleCamp.Unknown;
    }

    /// <summary>
    /// True for the twelve side-qualified buff/monster camps that make up a first
    /// clear (excludes scuttle and <see cref="JungleCamp.Unknown"/>).
    /// </summary>
    public static bool IsFirstClearCamp(JungleCamp camp)
        => camp is not (JungleCamp.Unknown or JungleCamp.ScuttleTop or JungleCamp.ScuttleBottom);
}
