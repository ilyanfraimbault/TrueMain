using Data;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.Services.Personas;

namespace TrueMain.Services.Ops.Navigation;

public static class OpsNavigationServiceCollectionExtensions
{
    /// <summary>Registers the admin sidebar's summary read (<c>GET /ops/nav-summary</c>, #2294).</summary>
    public static IServiceCollection AddTrueMainOpsNavigation(this IServiceCollection services)
    {
        services.AddScoped<INavSummaryQueryService>(provider => new NavSummaryQueryService(
            provider.GetRequiredService<TrueMainDbContext>(),
            provider.GetRequiredService<IPersonaHomonymQueryService>(),
            provider.GetRequiredService<IMemoryCache>(),
            provider.GetRequiredService<TimeProvider>()));
        return services;
    }
}
