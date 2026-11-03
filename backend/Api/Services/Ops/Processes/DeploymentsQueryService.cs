using Data.Configuration;
using Data.Ops.Mongo;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Processes;

/// <summary>Reads the deploys behind the admin charts' markers (#2297).</summary>
public interface IDeploymentsQueryService
{
    /// <summary>
    /// Every version change in <c>[fromUtc, toUtc]</c>. Both bounds default (the last
    /// <see cref="DeploymentsQueryService.DefaultWindow"/> up to now), and the caller has already
    /// checked that the window is well-formed (<see cref="DeploymentsQueryService.Validate"/>).
    /// </summary>
    Task<DeploymentsReadModel> GetAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken ct);
}

/// <inheritdoc />
public sealed class DeploymentsQueryService(IProcessBootStore store, TimeProvider timeProvider)
    : IDeploymentsQueryService
{
    /// <summary>The window when <c>from</c> is omitted.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(30);

    /// <summary>
    /// The widest window accepted: the longest chart on the portal (the database growth) spans a
    /// year.
    /// </summary>
    public static readonly TimeSpan MaxWindow = TimeSpan.FromDays(400);

    /// <summary>A validation message for an ill-formed window, or null when it is fine.</summary>
    public static string? Validate(DateTime? fromUtc, DateTime? toUtc)
    {
        if (fromUtc is { } from && toUtc is { } to)
        {
            if (from > to)
            {
                return "from must not be after to.";
            }

            if (to - from > MaxWindow)
            {
                return $"the window may span at most {MaxWindow.TotalDays:0} days.";
            }
        }

        return null;
    }

    public async Task<DeploymentsReadModel> GetAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken ct)
    {
        var to = ToUtc(toUtc) ?? timeProvider.GetUtcNow().UtcDateTime;
        var from = ToUtc(fromUtc) ?? to - DefaultWindow;
        if (to - from > MaxWindow)
        {
            from = to - MaxWindow;
        }

        var window = await store.GetDeploymentsAsync(from, to, ct);
        var deploys = DeploymentTimeline.Collapse(window.Before, window.InWindow);

        return new DeploymentsReadModel
        {
            FromUtc = from,
            ToUtc = to,
            Deployments = deploys.Select(deploy => new DeploymentReadModel
            {
                ProcessName = deploy.ProcessName,
                Role = deploy.Role,
                Version = deploy.Version,
                PreviousVersion = deploy.PreviousVersion,
                DeployedAtUtc = deploy.DeployedAtUtc
            }).ToList()
        };
    }

    private static DateTime? ToUtc(DateTime? value) => value?.Kind switch
    {
        null => null,
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.Value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
    };
}

/// <summary>Registration of the deploy markers' backend (#2297).</summary>
public static class DeploymentsServiceCollectionExtensions
{
    /// <summary>
    /// Records the Api's own boot in the boot log and registers the read behind
    /// <c>GET /ops/deployments</c>. The store comes from <c>AddMongoLogging</c>.
    /// </summary>
    public static IServiceCollection AddTrueMainDeployments(this IServiceCollection services)
    {
        services.AddProcessBootRecorder();
        services.AddScoped<IDeploymentsQueryService, DeploymentsQueryService>();
        return services;
    }
}
