using Microsoft.AspNetCore.Authentication;
using TrueMain.Configuration;
using TrueMain.Options;

namespace TrueMain.Authentication;

public static class ApiKeyAuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ops API-key scheme as the default authentication scheme — the one every
    /// <c>/ops</c> controller requires — and authorization. The log-ingest key's scheme is added
    /// next to it by <c>AddTrueMainLogIngest</c>.
    /// </summary>
    public static IServiceCollection AddTrueMainOpsAuthentication(this IServiceCollection services)
    {
        services.AddApiOptions<OpsOptions>();
        services
            .AddAuthentication(ApiKeyAuthenticationDefaults.Scheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationDefaults.Scheme,
                _ => { });
        services.AddAuthorization();
        return services;
    }
}
