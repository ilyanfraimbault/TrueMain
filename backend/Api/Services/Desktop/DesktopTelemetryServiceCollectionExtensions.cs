using TrueMain.Services.Ops.Desktop;

namespace TrueMain.Services.Desktop;

public static class DesktopTelemetryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the desktop app's telemetry (#1805): the write service behind
    /// <c>POST /desktop/telemetry</c> and <c>POST /internal/desktop/downloads</c>, and the read
    /// behind the admin page's <c>GET /ops/desktop/usage</c>. The store itself is registered with
    /// the other Mongo stores, by <c>AddMongoLogging</c>.
    /// </summary>
    public static IServiceCollection AddTrueMainDesktopTelemetry(this IServiceCollection services)
    {
        services.AddScoped<IDesktopTelemetryService, DesktopTelemetryService>();
        services.AddScoped<IDesktopUsageQueryService, DesktopUsageQueryService>();
        return services;
    }
}
