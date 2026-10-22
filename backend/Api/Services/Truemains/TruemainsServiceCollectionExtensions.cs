using TrueMain.Services.Ladder;
using TrueMain.Services.Truemains.Identity;
using TrueMain.Services.Truemains.Leaderboard;
using TrueMain.Services.Truemains.Lookup;
using TrueMain.Services.Truemains.Matches;
using TrueMain.Services.Truemains.PlayerChampions;
using TrueMain.Services.Truemains.Profile;

namespace TrueMain.Services.Truemains;

/// <summary>
/// Registers the player surface: the truemain profile, its matches, champions and LP curve,
/// the leaderboard and search, and the region's apex cutoffs the LP curve is drawn against.
/// </summary>
public static class TruemainsServiceCollectionExtensions
{
    public static IServiceCollection AddTrueMainTruemains(this IServiceCollection services)
    {
        // The single name-tag -> account lookup shared by every player-scoped route
        // (#1230), so they cannot disagree on which account a Riot ID means.
        services.AddScoped<TruemainAccountResolver>();
        // Shared by the truemain match feed and the composition provenance drawer (#940),
        // so a game renders as the same row on both.
        services.AddScoped<MatchSummaryHydrator>();
        services.AddScoped<IMatchSummariesQueryService, MatchSummariesQueryService>();
        services.AddScoped<IMatchDetailQueryService, MatchDetailQueryService>();
        services.AddScoped<IProfileQueryService, ProfileQueryService>();
        services.AddScoped<IPlayerChampionBuildsQueryService, PlayerChampionBuildsQueryService>();
        services.AddScoped<IPlayerChampionMatchupQueryService, PlayerChampionMatchupQueryService>();
        services.AddScoped<IPlayerChampionPerformanceQueryService, PlayerChampionPerformanceQueryService>();
        services.AddScoped<IRankHistoryQueryService, RankHistoryQueryService>();
        services.AddScoped<ITruemainActivityQueryService, TruemainActivityQueryService>();
        services.AddScoped<ITruemainsLeaderboardQueryService, TruemainsLeaderboardQueryService>();
        services.AddScoped<ISearchQueryService, SearchQueryService>();
        services.AddScoped<ITruemainLookupQueryService, TruemainLookupQueryService>();
        services.AddScoped<IApexCutoffsQueryService, ApexCutoffsQueryService>();
        return services;
    }
}
