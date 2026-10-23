namespace TrueMain.Services.Personas;

/// <summary>Registers the known-players surface (#2016): the public read, the ops write and the conflict queue (#2253).</summary>
public static class PersonaServiceCollectionExtensions
{
    public static IServiceCollection AddTrueMainPersonas(this IServiceCollection services)
    {
        services.AddScoped<IPersonaQueryService, PersonaQueryService>();
        services.AddScoped<PersonaConflictRecorder>();
        services.AddScoped<IPersonaUpsertService, PersonaUpsertService>();
        services.AddScoped<IPersonaConflictQueryService, PersonaConflictQueryService>();
        services.AddScoped<IPersonaConflictResolutionService, PersonaConflictResolutionService>();
        return services;
    }
}
