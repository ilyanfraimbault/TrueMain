using Core.Lol.Map;

namespace DevSeed;

/// <summary>
/// Hand-picked (x, y) coordinates that classify into each <see cref="MapZone"/>
/// under <see cref="LolMap"/>'s geometry, for generating kill-position rows that
/// the Roam metric (<c>ChampionRoamQueryService</c>) reads correctly. Verified
/// against the real <see cref="LolMap"/> at startup via <see cref="AssertValid"/>
/// rather than trusted blindly — if the map geometry ever changes, the tool fails
/// loudly instead of silently seeding kill positions that no longer land where
/// they're supposed to.
/// </summary>
public static class MapPoints
{
    public static readonly (int X, int Y) TopLane = (630, 13470);
    public static readonly (int X, int Y) MidLane = (7375, 7430);
    public static readonly (int X, int Y) BotLane = (13371, 635);
    public static readonly (int X, int Y) BlueSideJungle = (4377, 8940);
    public static readonly (int X, int Y) RedSideJungle = (10373, 5920);
    public static readonly (int X, int Y) BlueBase = (500, 500);
    public static readonly (int X, int Y) RedBase = (13500, 13500);

    public static void AssertValid()
    {
        Check(TopLane, MapZone.TopLane);
        Check(MidLane, MapZone.MidLane);
        Check(BotLane, MapZone.BotLane);
        Check(BlueBase, MapZone.BlueBase);
        Check(RedBase, MapZone.RedBase);

        if (LolMap.Classify(BlueSideJungle.X, BlueSideJungle.Y) != MapZone.Jungle
            || !LolMap.IsBlueSide(BlueSideJungle.X, BlueSideJungle.Y))
        {
            throw new InvalidOperationException(
                $"MapPoints.BlueSideJungle no longer classifies as blue-side jungle under LolMap — update the hardcoded coordinate.");
        }

        if (LolMap.Classify(RedSideJungle.X, RedSideJungle.Y) != MapZone.Jungle
            || LolMap.IsBlueSide(RedSideJungle.X, RedSideJungle.Y))
        {
            throw new InvalidOperationException(
                $"MapPoints.RedSideJungle no longer classifies as red-side jungle under LolMap — update the hardcoded coordinate.");
        }
    }

    private static void Check((int X, int Y) point, MapZone expected)
    {
        var actual = LolMap.Classify(point.X, point.Y);
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"MapPoints coordinate {point} classifies as {actual}, not {expected} — LolMap's geometry changed; update the hardcoded coordinate.");
        }
    }

    /// <summary>The player's own lane point, for an in-lane (non-roam) kill participation.</summary>
    public static (int X, int Y) OwnLane(string position) => position switch
    {
        "TOP" => TopLane,
        "MIDDLE" => MidLane,
        "BOTTOM" or "UTILITY" => BotLane,
        _ => MidLane,
    };

    /// <summary>A roam point: the enemy jungle relative to the player's own side — always classifies as a roam via <see cref="LolMap.IsRoam"/>.</summary>
    public static (int X, int Y) EnemyJungle(bool ownIsBlueSide) => ownIsBlueSide ? RedSideJungle : BlueSideJungle;
}
