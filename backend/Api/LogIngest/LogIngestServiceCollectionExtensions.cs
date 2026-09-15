using Microsoft.AspNetCore.Authentication;
using TrueMain.Authentication;
using TrueMain.Options;

namespace TrueMain.LogIngest;

public static class LogIngestServiceCollectionExtensions
{
    /// <summary>
    /// Registers <c>POST /internal/logs</c>'s key, its authentication scheme and the
    /// service behind it (#1556). The key is optional: unset turns ingestion off
    /// rather than failing the boot, see <see cref="LogIngestOptions"/>.
    /// </summary>
    public static IServiceCollection AddTrueMainLogIngest(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<LogIngestOptions>()
            .Bind(configuration.GetSection(LogIngestOptions.SectionName))
            .Validate(
                options => !options.IsEnabled || options.ApiKey!.Length >= LogIngestOptions.MinimumKeyLength,
                $"LogIngest:ApiKey must be at least {LogIngestOptions.MinimumKeyLength} characters when set; "
                + "leave it empty to turn log ingestion off.")
            .ValidateOnStart();
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, LogIngestAuthenticationHandler>(
                LogIngestAuthenticationDefaults.Scheme,
                _ => { });
        services.AddScoped<ILogIngestService, LogIngestService>();
        return services;
    }
}
