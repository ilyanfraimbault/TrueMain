using Core.Options;
using Data.Configuration;
using TrueMain.Configuration;

namespace TrueMain.Options;

public static class MainAnalysisServiceCollectionExtensions
{
    /// <summary>
    /// Binds the main-definition settings the Ingestor decides mains with, for the features that
    /// read its verdicts: the dedication score, the leaderboard, the champion reads, the ops panels.
    /// </summary>
    public static IServiceCollection AddMainAnalysisOptions(this IServiceCollection services)
        => services.AddOptionsSection<MainAnalysisOptions>(
            "MainAnalysis",
            SharedEffectiveConfigurationSections.MainAnalysis,
            options => options
                .Validate(main => Enum.IsDefined(main.QueueId), "MainAnalysis:QueueId must be a defined LolQueueId.")
                // The dedication score divides by (1 - PlayRateFloor) (#869), so a floor of 1
                // would invert its rescale; the Ingestor rejects it too (#930).
                .Validate(main => main.PlayRateFloor is >= 0 and < 1, "MainAnalysis:PlayRateFloor must be in [0, 1).")
                .Validate(
                    main => main.PlayRateFloor <= main.PlayRateThreshold,
                    "MainAnalysis:PlayRateFloor must be <= PlayRateThreshold."));
}
