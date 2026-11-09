using Microsoft.Extensions.Options;
using TrueMain.Configuration;
using TrueMain.Options;
using AspNetCorsOptions = Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions;

namespace TrueMain.Http;

public static class FrontendCorsServiceCollectionExtensions
{
    private const string PolicyName = "FrontendCors";

    /// <summary>
    /// Builds the frontends' CORS policy from <see cref="FrontendCorsOptions"/>. An empty origin
    /// list still builds a valid policy that allows nothing, so outside Development it fails the
    /// boot: production ships an empty array in <c>appsettings.json</c> and must be given its origins.
    /// </summary>
    public static IServiceCollection AddTrueMainCors(this IServiceCollection services, IHostEnvironment environment)
    {
        var isDevelopment = environment.IsDevelopment();
        services.AddApiOptions<FrontendCorsOptions>();
        services.AddOptions<FrontendCorsOptions>()
            .Validate(
                options => isDevelopment || options.Origins.Length > 0,
                "Cors:Origins must contain at least one origin outside the Development environment; "
                + "an empty list ships a no-op CORS policy that silently rejects the frontend.");
        services.AddCors();
        services.AddOptions<AspNetCorsOptions>()
            .Configure<IOptions<FrontendCorsOptions>>((corsPolicies, frontend) =>
                corsPolicies.AddPolicy(PolicyName, policy =>
                {
                    var builder = policy.AllowAnyHeader().AllowAnyMethod();
                    if (frontend.Value.Origins.Length > 0)
                    {
                        builder.WithOrigins(frontend.Value.Origins);
                    }
                }));
        return services;
    }

    /// <summary>
    /// Applies the policy. Under Development an empty list boots, so it is flagged here rather than
    /// read as a working CORS setup.
    /// </summary>
    public static WebApplication UseTrueMainCors(this WebApplication app)
    {
        if (app.Environment.IsDevelopment()
            && app.Services.GetRequiredService<IOptions<FrontendCorsOptions>>().Value.Origins.Length == 0)
        {
            app.Logger.LogWarning(
                "Cors:Origins is empty; the {Policy} policy allows no cross-origin browser request. Set Cors:Origins in configuration to let the frontend reach the API.",
                PolicyName);
        }

        app.UseCors(PolicyName);
        return app;
    }
}
