namespace TrueMain.Services.Personas;

/// <summary>Registers the known-players surface (#2016): the public read and the ops write.</summary>
public static class PersonaServiceCollectionExtensions
{
    public static IServiceCollection AddTrueMainPersonas(this IServiceCollection services)
    {
        services.AddScoped<IPersonaQueryService, PersonaQueryService>();
        services.AddScoped<IPersonaUpsertService, PersonaUpsertService>();
        return services;
    }
}
