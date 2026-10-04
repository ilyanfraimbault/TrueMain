using TrueMain.Services.Truemains.PlayerChampions;

namespace TrueMain.Options;

/// <summary>
/// Binds and validates <see cref="ChampionsListOptions"/>.
/// </summary>
/// <remarks>
/// Extracted from <c>Program.cs</c>, which sits at the size guardrail's limit: the
/// section carries one startup check per floor, and they read better together.
/// </remarks>
public static class ChampionsListOptionsServiceCollectionExtensions
{
    public static IServiceCollection AddChampionsListOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ChampionsListOptions>()
            .Bind(configuration.GetSection(ChampionsListOptions.SectionName))
            .Validate(options => options.MinSampleGames >= 0, "ChampionsList:MinSampleGames must be >= 0.")
            .Validate(options => options.MinBuildSampleGames >= 0, "ChampionsList:MinBuildSampleGames must be >= 0.")
            .Validate(options => options.MinServablePatchLines >= 0, "ChampionsList:MinServablePatchLines must be >= 0.")
            .Validate(options => options.MinMatchupGames >= 0, "ChampionsList:MinMatchupGames must be >= 0.")
            // A share, so out of [0,1) it stops meaning anything: 1 would demand a single
            // opponent account for every game the champion ever played, which no matchup can.
            .Validate(
                options => options.MinMatchupPlayRate is >= 0d and < 1d,
                "ChampionsList:MinMatchupPlayRate must be in [0, 1).")
            .Validate(options => options.MinDecidedLaneGames >= 0, "ChampionsList:MinDecidedLaneGames must be >= 0.")
            // A share, so out of [0,1) it stops meaning anything: 1 would demand a pairing
            // present in every game the champion ever played, which no pairing is.
            .Validate(
                options => options.MinSynergyPlayRate is >= 0d and < 1d,
                "ChampionsList:MinSynergyPlayRate must be in [0, 1).")
            // A share too, but 1 is a meaningful setting here: SynergyBaselineSet.IsRealLane
            // divides a champion's games in one lane by its games across all lanes, which
            // is exactly 1 for a mono-lane champion. So [0, 1], closed on both ends.
            .Validate(
                options => options.MinSynergyPartnerLanePlayRate is >= 0d and <= 1d,
                "ChampionsList:MinSynergyPartnerLanePlayRate must be in [0, 1].")
            .Validate(options => options.MinPlayerMatchupGames >= 0, "ChampionsList:MinPlayerMatchupGames must be >= 0.")
            .Validate(
                options => options.MinPlayerBuildGames is >= 1 and <= PlayerChampionPerformanceQueryService.Window,
                $"ChampionsList:MinPlayerBuildGames must be in [1, {PlayerChampionPerformanceQueryService.Window}].")
            .Validate(options => options.MaxLanesPerChampion >= 0, "ChampionsList:MaxLanesPerChampion must be >= 0.")
            .Validate(
                options => options.MinSecondaryLanePlayRate is >= 0 and <= 1,
                "ChampionsList:MinSecondaryLanePlayRate must be a share between 0 and 1.")
            .ValidateOnStart();

        return services;
    }
}
