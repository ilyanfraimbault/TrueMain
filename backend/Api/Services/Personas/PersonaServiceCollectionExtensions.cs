namespace TrueMain.Services.Personas;

/// <summary>
/// Registers the known-players surface (#2016): the public read, the ops write, the conflict queue
/// (#2253) and the operator's edits (#2019).
/// </summary>
public static class PersonaServiceCollectionExtensions
{
    public static IServiceCollection AddTrueMainPersonas(this IServiceCollection services)
    {
        services.AddScoped<IPersonaQueryService, PersonaQueryService>();
        services.AddScoped<PersonaConflictRecorder>();
        services.AddScoped<IPersonaUpsertService, PersonaUpsertService>();
        services.AddScoped<IPersonaConflictQueryService, PersonaConflictQueryService>();
        services.AddScoped<IPersonaConflictResolutionService, PersonaConflictResolutionService>();
        services.AddScoped<PersonaAudit>();
        services.AddScoped<IPersonaAdminQueryService, PersonaAdminQueryService>();
        services.AddScoped<IPersonaEditService, PersonaEditService>();
        services.AddScoped<IPersonaRemovalService, PersonaRemovalService>();
        return services;
    }
}
