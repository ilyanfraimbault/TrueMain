using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace TrueMain.Services.Champions.Warmup;

/// <summary>
/// Holds <c>/readyz</c> down until the first warm pass is done, which keeps the public site on
/// the maintenance page (#2425): the edge asks web's <c>/api/readyz</c>, which asks this.
/// </summary>
/// <remarks>
/// <para><see cref="HealthStatus.Degraded"/>, mapped to a 503 on <c>/readyz</c>, because this is not a
/// failure: it is how web tells "still warming" from "down" and does not report it as one.</para>
/// <para>The hold is logged here, once when it starts and once when it ends, rather than by the
/// health check service on every probe (see
/// <see cref="ChampionWarmupServiceCollectionExtensions.HealthCheckServiceCategory"/>).</para>
/// </remarks>
public sealed partial class ChampionWarmupHealthCheck(
    ChampionReadVersion version,
    IOptions<ChampionWarmupOptions> options,
    TimeProvider clock,
    ILogger<ChampionWarmupHealthCheck> logger) : IHealthCheck
{
    public const string Name = "champion-warmup";

    private const int Unknown = 0;
    private const int Holding = 1;
    private const int Open = 2;

    private int _state = Unknown;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var budget = options.Value.ReadyBudgetSeconds;
        var published = version.HasPublished;
        var waited = clock.GetUtcNow() - version.StartedAt;
        if (published || waited >= TimeSpan.FromSeconds(budget))
        {
            if (Interlocked.Exchange(ref _state, Open) != Open)
            {
                if (published)
                {
                    LogOpenedWarm(logger, (long)waited.TotalSeconds);
                }
                else
                {
                    LogOpenedCold(logger, budget);
                }
            }

            return Task.FromResult(HealthCheckResult.Healthy());
        }

        if (Interlocked.CompareExchange(ref _state, Holding, Unknown) == Unknown)
        {
            LogHeld(logger, budget);
        }

        return Task.FromResult(HealthCheckResult.Degraded("The first warm pass is still running."));
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Readiness held behind the maintenance page until the first champion warm pass publishes (budget {BudgetSeconds}s)")]
    private static partial void LogHeld(ILogger logger, int budgetSeconds);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Readiness open: the first champion warm pass published after {WaitedSeconds}s")]
    private static partial void LogOpenedWarm(ILogger logger, long waitedSeconds);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Readiness open on a cold cache: the first champion warm pass did not publish within its {BudgetSeconds}s budget")]
    private static partial void LogOpenedCold(ILogger logger, int budgetSeconds);
}
