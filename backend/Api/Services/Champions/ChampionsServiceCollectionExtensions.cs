using Data.Statics;
using TrueMain.Configuration;
using TrueMain.Options;
using TrueMain.Services.Champions.Builds;
using TrueMain.Services.Champions.Composition;
using TrueMain.Services.Champions.Directory;
using TrueMain.Services.Champions.Draft;
using TrueMain.Services.Champions.ItemRoutes;
using TrueMain.Services.Champions.Mains;
using TrueMain.Services.Champions.Matchups;
using TrueMain.Services.Champions.NextItem;
using TrueMain.Services.Champions.Profiles;
using TrueMain.Services.Champions.Progression;
using TrueMain.Services.Champions.Scopes;
using TrueMain.Services.Champions.Synergies;
using TrueMain.Services.Champions.Warmup;

namespace TrueMain.Services.Champions;

/// <summary>
/// Registers the champion surface: the public champion pages, the composition recommender and the
/// desktop's draft, next-item and damage-profile reads, with the static game data they read.
/// </summary>
public static class ChampionsServiceCollectionExtensions
{
    public static IServiceCollection AddTrueMainChampions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMainAnalysisOptions();
        services.AddApiOptions<ChampionsListOptions>();
        services.AddApiOptions<ChampionTierOptions>();
        services.AddApiOptions<CompositionSearchOptions>();

        // The one door every champion read goes through: shared cache + single flight, keyed by
        // the ingestor's aggregation version rather than by a clock (#1368).
        services.AddScoped<IChampionAggregationStamp, ChampionAggregationStamp>();
        services.AddChampionWarmup(configuration);
        services.AddScoped<IChampionReadCache, ChampionReadCache>();

        services.AddScoped<IChampionSummariesQueryService, ChampionSummariesQueryService>();
        services.AddScoped<IChampionTierListQueryService, ChampionTierListQueryService>();
        services.AddScoped<IChampionOverviewQueryService, ChampionOverviewQueryService>();
        services.AddScoped<IChampionDirectoryQueryService, ChampionDirectoryQueryService>();
        services.AddScoped<IChampionTrendQueryService, ChampionTrendQueryService>();
        services.AddScoped<IChampionBuildsQueryService, ChampionBuildsQueryService>();
        services.AddScoped<IChampionItemContextQueryService, ChampionItemContextQueryService>();
        services.AddScoped<IChampionMatchupQueryService, ChampionMatchupQueryService>();
        services.AddScoped<IChampionMatchupBuildsQueryService, ChampionMatchupBuildsQueryService>();
        services.AddScoped<IChampionSynergyQueryService, ChampionSynergyQueryService>();
        services.AddScoped<IChampionScalingQueryService, ChampionScalingQueryService>();
        services.AddScoped<IChampionMainsComparisonQueryService, ChampionMainsComparisonQueryService>();

        // Shared by the composition recommendation (#921) and the matchup-scoped champion page
        // (#923) so a game means the same build to both.
        services.AddScoped<ParticipantBuildFactsLoader>();
        services.AddScoped<ICompositionMatchQueryService, CompositionMatchQueryService>();
        services.AddScoped<ICompositionBuildQueryService, CompositionBuildQueryService>();
        services.AddScoped<ICompositionGamesQueryService, CompositionGamesQueryService>();
        services.AddScoped<ICompositionLaneOutcomeQueryService, CompositionLaneOutcomeQueryService>();
        services.AddScoped<ICompositionRecommendationQueryService, CompositionRecommendationQueryService>();

        // The draft assistant (#1674, #1675). Its lane prior reads the ALLY synergy baselines
        // rather than the scope table, which only covers champions our population mains.
        services.AddScoped<ILanePriorQueryService, LanePriorQueryService>();
        services.AddScoped<IDraftRecommendationQueryService, DraftRecommendationQueryService>();
        services.AddScoped<IDraftPatchScopeResolver, DraftPatchScopeResolver>();
        services.AddScoped<IDraftLaneReader, DraftLaneReader>();
        services.AddScoped<IDraftEnemyReader, DraftEnemyReader>();
        services.AddScoped<IDraftTeamFitReader, DraftTeamFitReader>();
        services.AddScoped<IDraftBanQueryService, DraftBanQueryService>();

        services.AddScoped<INextItemQueryService, NextItemQueryService>();
        services.AddScoped<IItemRouteQueryService, ItemRouteQueryService>();
        services.AddScoped<IChampionDamageProfileQueryService, ChampionDamageProfileQueryService>();

        services.AddStaticGameData();
        return services;
    }

    /// <summary>
    /// The static game data providers on one named client, plain: the Api reads them inside
    /// the champion read cache, so a CDN hiccup costs one degraded answer, not a retry storm.
    /// </summary>
    private static void AddStaticGameData(this IServiceCollection services)
    {
        services.AddHttpClient(StaticGameDataServiceCollectionExtensions.ClientName);
        StaticGameDataServiceCollectionExtensions.AddStaticGameData(
            services,
            provider => provider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(StaticGameDataServiceCollectionExtensions.ClientName));
    }
}
