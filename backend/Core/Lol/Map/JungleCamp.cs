namespace Core.Lol.Map;

/// <summary>
/// A Summoner's Rift jungle camp, side-qualified (blue/red half of the map).
/// Used to turn a per-minute jungler (x, y) position into the camp it sits on
/// (issue #535). Scuttle crabs live on the river and are shared, so they are not
/// side-qualified.
/// </summary>
public enum JungleCamp
{
    Unknown = 0,

    BlueGromp,
    BlueBlueBuff,
    BlueWolves,
    BlueRaptors,
    BlueRedBuff,
    BlueKrugs,

    RedGromp,
    RedBlueBuff,
    RedWolves,
    RedRaptors,
    RedRedBuff,
    RedKrugs,

    ScuttleTop,
    ScuttleBottom
}
