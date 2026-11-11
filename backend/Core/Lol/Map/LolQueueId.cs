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

/// <summary>
/// The league-v4 queue keys (<c>queue</c> in Riot's league endpoints), the string twin of
/// <see cref="LolQueueId"/>. Only ranked solo/duo is stored (#680), so it is the only one.
/// </summary>
public static class LeagueQueueType
{
    /// <summary>Ranked solo/duo, <see cref="LolQueueId.RankedSoloDuo"/>'s league key.</summary>
    public const string RankedSolo = "RANKED_SOLO_5x5";
}
