using Microsoft.AspNetCore.Authentication;
using TrueMain.Authentication;
using TrueMain.Configuration;
using TrueMain.Options;

namespace TrueMain.LogIngest;

public static class LogIngestServiceCollectionExtensions
{
    /// <summary>
    /// Registers <c>POST /internal/logs</c>'s key, its authentication scheme and the
    /// service behind it (#1556). The key is optional: unset turns ingestion off
    /// rather than failing the boot, see <see cref="LogIngestOptions"/>.
    /// </summary>
    public static IServiceCollection AddTrueMainLogIngest(this IServiceCollection services)
    {
        services.AddApiOptions<LogIngestOptions>();
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, LogIngestAuthenticationHandler>(
                LogIngestAuthenticationDefaults.Scheme,
                _ => { });
        services.AddScoped<ILogIngestService, LogIngestService>();
        return services;
    }
}
