namespace Core.Lol.Map;

public static class LolPositionExtensions
{
    public static LolPosition Parse(string? teamPosition)
        => teamPosition?.Trim().ToUpperInvariant() switch
        {
            "TOP" => LolPosition.Top,
            "JUNGLE" => LolPosition.Jungle,
            "MIDDLE" => LolPosition.Middle,
            "BOTTOM" => LolPosition.Bottom,
            "UTILITY" => LolPosition.Utility,
            _ => LolPosition.Unknown
        };

    public static string? ToRiotString(this LolPosition position)
        => position switch
        {
            LolPosition.Top => "TOP",
            LolPosition.Jungle => "JUNGLE",
            LolPosition.Middle => "MIDDLE",
            LolPosition.Bottom => "BOTTOM",
            LolPosition.Utility => "UTILITY",
            _ => null
        };
}
