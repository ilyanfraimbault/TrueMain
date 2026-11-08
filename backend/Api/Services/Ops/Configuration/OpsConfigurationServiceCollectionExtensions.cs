namespace TrueMain.Services.Ops.Configuration;

public static class OpsConfigurationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the configuration page's read. The Api's sections are the ones its features
    /// registered with their options (<c>AddApiOptions</c>), so a section is listed where it is bound.
    /// </summary>
    public static IServiceCollection AddTrueMainOpsConfiguration(this IServiceCollection services)
    {
        services.AddScoped<IEffectiveConfigurationQueryService, EffectiveConfigurationQueryService>();
        return services;
    }
}
