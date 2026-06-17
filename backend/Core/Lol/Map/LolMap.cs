namespace Core.Lol.Map;

/// <summary>
/// Summoner's Rift coordinate geometry for the Riot match timeline.
///
/// Classifies a timeline (x, y) position into a coarse <see cref="MapZone"/>.
/// The classification is a documented heuristic built on the verified map
/// bounds and the map's diagonals (mid lane runs blue-base→red-base, the river
/// is the anti-diagonal, top/bot lanes hug the opposite edges). Band widths are
/// tunable constants; they were checked against known turret coordinates
/// (e.g. blue top outer turret, blue mid inhibitor, red bot base turret).
///
/// Bounds source: HextechDocs map data — x ∈ [-120, 14870], y ∈ [-120, 14980].
/// </summary>
public static class LolMap
{
    public const int MinCoordinate = -120;
    public const int MaxX = 14870;
    public const int MaxY = 14980;

    // Normalized band half-widths (fraction of map size, 0..1). Heuristic.
    private const double LaneHalfWidth = 0.09;
    private const double RiverHalfWidth = 0.07;
    private const double BaseRadius = 0.14;

    public static MapZone Classify(int x, int y)
    {
        var nx = Normalize(x, MaxX);
        var ny = Normalize(y, MaxY);

        if (nx < BaseRadius && ny < BaseRadius)
        {
            return MapZone.BlueBase;
        }

        if (nx > 1 - BaseRadius && ny > 1 - BaseRadius)
        {
            return MapZone.RedBase;
        }

        // Distance to each lane. Mid = main diagonal (nx ≈ ny); top/bot lanes hug
        // the opposite L-shaped edges and only exist on their side of the diagonal.
        var nearestLaneDistance = Math.Abs(nx - ny);
        var nearestLane = MapZone.MidLane;

        if (ny >= nx)
        {
            var distanceTop = Math.Min(nx, 1 - ny);
            if (distanceTop < nearestLaneDistance)
            {
                nearestLaneDistance = distanceTop;
                nearestLane = MapZone.TopLane;
            }
        }

        if (ny <= nx)
        {
            var distanceBot = Math.Min(1 - nx, ny);
            if (distanceBot < nearestLaneDistance)
            {
                nearestLaneDistance = distanceBot;
                nearestLane = MapZone.BotLane;
            }
        }

        if (nearestLaneDistance <= LaneHalfWidth)
        {
            return nearestLane;
        }

        if (Math.Abs(nx + ny - 1) <= RiverHalfWidth)
        {
            return MapZone.River;
        }

        return MapZone.Jungle;
    }

    /// <summary>
    /// Blue team occupies the bottom-left half of the map (below the river
    /// anti-diagonal); red occupies the top-right half. The river boundary itself
    /// (nx + ny == 1) resolves to red side — an academic tie-break, since exact
    /// equality on real integer coordinates effectively never occurs.
    /// </summary>
    public static bool IsBlueSide(int x, int y)
        => Normalize(x, MaxX) + Normalize(y, MaxY) < 1.0;

    private static double Normalize(int value, int max)
    {
        var span = (double)(max - MinCoordinate);
        var normalized = (value - MinCoordinate) / span;
        return Math.Clamp(normalized, 0.0, 1.0);
    }
}
