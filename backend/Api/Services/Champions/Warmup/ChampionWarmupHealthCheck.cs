using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace TrueMain.Services.Champions.Warmup;

/// <summary>
/// Holds <c>/readyz</c> down until the first warm pass is done, which keeps the public site on
/// the maintenance page (#2425): the edge asks web's <c>/api/readyz</c>, which asks this.
/// </summary>
/// <remarks>
/// <see cref="HealthStatus.Degraded"/>, mapped to a 503 on <c>/readyz</c>, because this is not a
/// failure: it is how web tells "still warming" from "down" and does not report it as one.
/// </remarks>
public sealed class ChampionWarmupHealthCheck(
    ChampionReadVersion version,
    IOptions<ChampionWarmupOptions> options,
    TimeProvider clock) : IHealthCheck
{
    public const string Name = "champion-warmup";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var budget = TimeSpan.FromSeconds(options.Value.ReadyBudgetSeconds);
        var ready = version.HasPublished || clock.GetUtcNow() - version.StartedAt >= budget;
        return Task.FromResult(ready
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded("The first warm pass is still running."));
    }
}
