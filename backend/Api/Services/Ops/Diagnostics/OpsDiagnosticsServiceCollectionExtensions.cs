namespace TrueMain.Services.Ops.Diagnostics;

public static class OpsDiagnosticsServiceCollectionExtensions
{
    /// <summary>Registers the logs, audit and crash reads. Their stores come from <c>AddMongoLogging</c>.</summary>
    public static IServiceCollection AddTrueMainOpsDiagnostics(this IServiceCollection services)
    {
        services.AddScoped<ILogsQueryService, LogsQueryService>();
        services.AddScoped<IAuditEventsQueryService, AuditEventsQueryService>();
        services.AddScoped<ICrashesQueryService, CrashesQueryService>();
        return services;
    }
}
