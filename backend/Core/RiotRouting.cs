using Core.Lol.Identifiers;

namespace Core;

/// <summary>
/// Routing helpers mapping Riot <see cref="PlatformRoute"/> and <see cref="RegionalRoute"/>
/// values to their regional shards and API host segments.
/// </summary>
public static class RiotRouting
{
    /// <summary>
    /// Resolves the <see cref="RegionalRoute"/> that serves the given platform shard.
    /// </summary>
    /// <param name="platform">The platform route to resolve.</param>
    /// <returns>The regional route the platform belongs to.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the platform is not supported.</exception>
    public static RegionalRoute ToRegional(this PlatformRoute platform) =>
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

    /// <summary>
    /// Returns the platform host segment (e.g. <c>"euw1"</c>) used in Riot API URLs.
    /// </summary>
    /// <param name="platform">The platform route to map.</param>
    /// <returns>The lowercase platform host segment.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the platform is not supported.</exception>
    public static string ToPlatformHost(this PlatformRoute platform) =>
        platform switch
        {
            PlatformRoute.BR1 => "br1",
            PlatformRoute.EUN1 => "eun1",
            PlatformRoute.EUW1 => "euw1",
            PlatformRoute.JP1 => "jp1",
            PlatformRoute.KR => "kr",
            PlatformRoute.LA1 => "la1",
            PlatformRoute.LA2 => "la2",
            PlatformRoute.NA1 => "na1",
            PlatformRoute.OC1 => "oc1",
            PlatformRoute.PH2 => "ph2",
            PlatformRoute.RU => "ru",
            PlatformRoute.SG2 => "sg2",
            PlatformRoute.TH2 => "th2",
            PlatformRoute.TR1 => "tr1",
            PlatformRoute.TW2 => "tw2",
            PlatformRoute.VN2 => "vn2",
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported platform route.")
        };

    /// <summary>
    /// Returns the regional host segment (e.g. <c>"europe"</c>) used in Riot API URLs.
    /// </summary>
    /// <param name="region">The regional route to map.</param>
    /// <returns>The lowercase regional host segment.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the region is not supported.</exception>
    public static string ToRegionalHost(this RegionalRoute region) =>
        region switch
        {
            RegionalRoute.Europe => "europe",
            RegionalRoute.Americas => "americas",
            RegionalRoute.Asia => "asia",
            RegionalRoute.Sea => "sea",
            _ => throw new ArgumentOutOfRangeException(nameof(region), region, "Unsupported regional route.")
        };
}
