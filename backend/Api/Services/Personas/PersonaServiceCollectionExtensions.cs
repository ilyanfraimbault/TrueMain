using TrueMain.Configuration;
using TrueMain.Options;
using TrueMain.Services.Personas.Pictures;

namespace TrueMain.Services.Personas;

/// <summary>
/// Registers the known-players surface (#2016): the public read, the ops write, the conflict queue
/// (#2253), its bulk resolution (#2295) and its channel conflicts (#2286), the operator's edits (#2019) and the homonyms queue (#2271).
/// </summary>
public static class PersonaServiceCollectionExtensions
{
    public static IServiceCollection AddTrueMainPersonas(this IServiceCollection services)
    {
        services.AddScoped<IPersonaQueryService, PersonaQueryService>();
        services.AddScoped<PersonaConflictRecorder>();
        services.AddScoped<IPersonaUpsertService, PersonaUpsertService>();
        services.AddScoped<IPersonaConflictQueryService, PersonaConflictQueryService>();
        services.AddScoped<PersonaMerger>();
        services.AddScoped<IPersonaConflictResolutionService, PersonaConflictResolutionService>();
        services.AddScoped<IPersonaConflictBulkResolutionService, PersonaConflictBulkResolutionService>();
        services.AddScoped<PersonaChannelConflictFinder>();
        services.AddScoped<IPersonaChannelConflictQueryService, PersonaChannelConflictQueryService>();
        services.AddScoped<IPersonaAutoMergeService, PersonaAutoMergeService>();
        services.AddHostedService<PersonaAutoMergeStartupSweep>();
        services.AddScoped<IPersonaHomonymQueryService, PersonaHomonymQueryService>();
        services.AddScoped<IPersonaHomonymService, PersonaHomonymService>();
        services.AddScoped<PersonaAudit>();
        services.AddScoped<IPersonaAdminQueryService, PersonaAdminQueryService>();
        services.AddScoped<IPersonaEditService, PersonaEditService>();
        services.AddScoped<IPersonaRemovalService, PersonaRemovalService>();
        services.AddPersonaPictures();
        return services;
    }

    private static void AddPersonaPictures(this IServiceCollection services)
    {
        services.AddApiOptions<PersonaPictureOptions>();

        // No request loggers, like the alerter's: the secret-bearing requests stay out of the logs entirely.
        services.AddHttpClient(PersonaPictureReader.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("TrueMain/1.0 (+https://truemain.lol)");
            })
            .RemoveAllLoggers();
        services.AddSingleton<IPersonaPictureReader, YouTubePictureReader>();
        services.AddSingleton<IPersonaPictureReader, TwitchPictureReader>();
        services.AddSingleton<IPersonaPictureReader, KickPictureReader>();
        services.AddSingleton<IPersonaPictureReader, SoopPictureReader>();
        services.AddSingleton<IPersonaPictureReader, ChzzkPictureReader>();
        services.AddSingleton<PersonaPictureResolver>();
        services.AddHostedService<PersonaPictureWorker>();
    }
}
