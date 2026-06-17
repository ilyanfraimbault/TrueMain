namespace Core.Lol.Map;

/// <summary>
/// Coarse Summoner's Rift region a timeline (x, y) position falls into.
/// Used to turn raw event/frame coordinates into meaning (e.g. roam = kills
/// outside the player's lane). See issue #538.
/// </summary>
public enum MapZone
{
    Unknown = 0,
    BlueBase,
    RedBase,
    TopLane,
    MidLane,
    BotLane,
    River,
    Jungle
}
