using Data.Configuration;
namespace TrueMain.Services.Ops.Processes;

public static class OpsProcessesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the process runs and their baselines, the external scripts' run reports, and the
    /// deploy markers: the Api's own boot is recorded in the boot log behind <c>GET /ops/deployments</c>
    /// (#2297). The stores come from <c>AddMongoLogging</c>.
    /// </summary>
    public static IServiceCollection AddTrueMainOpsProcesses(this IServiceCollection services)
    {
        services.AddScoped<IProcessRunBaselineService, ProcessRunBaselineService>();
        services.AddScoped<IProcessRunsQueryService, ProcessRunsQueryService>();
        services.AddScoped<IProcessIterationsQueryService, ProcessIterationsQueryService>();
        services.AddScoped<IExternalProcessRunService, ExternalProcessRunService>();
        services.AddProcessBootRecorder();
        services.AddScoped<IDeploymentsQueryService, DeploymentsQueryService>();
        return services;
    }
}
