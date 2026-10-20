namespace TrueMain.Services.Truemains.Live;

public static class LiveGameServiceCollectionExtensions
{
    /// <summary>
    /// Registers the profile's live tab (#2103): the read behind <c>GET /truemains/{nameTag}/live</c>
    /// and the Riot client it reads spectator-v5 and league-v4 through, keyed by the same
    /// <c>Riot:ApiKey</c> as the ingestor. Without a key the tab answers "unavailable".
    /// </summary>
    public static IServiceCollection AddTrueMainLiveGames(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RiotLiveOptions>(configuration.GetSection(RiotLiveOptions.SectionName));
        services.AddHttpClient<IRiotLiveClient, RiotLiveClient>(client => client.Timeout = TimeSpan.FromSeconds(5));
        services.AddScoped<LiveGameHydrator>();
        services.AddScoped<ILiveGameQueryService, LiveGameQueryService>();
        return services;
    }
}
