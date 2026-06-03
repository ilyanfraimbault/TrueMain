namespace Core.Lol.Map;

/// <summary>
/// Riot ranked/queue ids. Backed by their stable Riot numeric values so the
/// enum can cross the int-typed persistence boundary with an explicit cast.
/// </summary>
public enum LolQueueId
{
    RankedSoloDuo = 420,
    Normal = 430,
    RankedFlex = 440,
    Aram = 450,
    Clash = 700,
}
