namespace Core;

public static class RiotRouting
{
    public static RegionalRoute FromPlatform(PlatformRoute platform) =>
        platform switch
        {
            PlatformRoute.EUW1 => RegionalRoute.Europe,
            PlatformRoute.EUN1 => RegionalRoute.Europe,
            PlatformRoute.RU => RegionalRoute.Europe,
            PlatformRoute.TR1 => RegionalRoute.Europe,

            PlatformRoute.NA1 => RegionalRoute.Americas,
            PlatformRoute.BR1 => RegionalRoute.Americas,
            PlatformRoute.LA1 => RegionalRoute.Americas,
            PlatformRoute.LA2 => RegionalRoute.Americas,
            PlatformRoute.OC1 => RegionalRoute.Americas,

            PlatformRoute.KR => RegionalRoute.Asia,
            PlatformRoute.JP1 => RegionalRoute.Asia,

            PlatformRoute.PH2 => RegionalRoute.Sea,
            PlatformRoute.SG2 => RegionalRoute.Sea,
            PlatformRoute.TH2 => RegionalRoute.Sea,
            PlatformRoute.TW2 => RegionalRoute.Sea,
            PlatformRoute.VN2 => RegionalRoute.Sea,

            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported platform route.")
        };

    public static string ToRegionalHost(RegionalRoute region) =>
        region switch
        {
            RegionalRoute.Europe => "europe",
            RegionalRoute.Americas => "americas",
            RegionalRoute.Asia => "asia",
            RegionalRoute.Sea => "sea",
            _ => throw new ArgumentOutOfRangeException(nameof(region), region, "Unsupported regional route.")
        };
}
