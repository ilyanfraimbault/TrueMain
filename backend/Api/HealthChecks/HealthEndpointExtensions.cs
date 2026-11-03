using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TrueMain.HealthChecks;

public static class HealthEndpointExtensions
{
    /// <summary>
    /// <c>/healthz</c>, which only says the process runs, and <c>/readyz</c>, which the site's
    /// maintenance page hangs on. <see cref="HealthStatus.Degraded"/> on the latter is the
    /// warm-up still running, which is not ready either (see
    /// <see cref="Services.Champions.Warmup.ChampionWarmupHealthCheck"/>); web reads the body to
    /// tell it from a failure.
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/healthz", new HealthCheckOptions
        {
            Predicate = _ => false,
        }).DisableRateLimiting();

        endpoints.MapHealthChecks("/readyz", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        }).DisableRateLimiting();

        return endpoints;
    }
}
