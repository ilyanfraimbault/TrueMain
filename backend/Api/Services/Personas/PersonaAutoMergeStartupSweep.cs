namespace TrueMain.Services.Personas;

/// <summary>
/// Runs the same-name conflict sweep (<see cref="PersonaAutoMergeService"/>) once, shortly after the
/// API starts (#2273). The intake only sweeps after a batch that raised a conflict, and the scrapers
/// run weekly: without this, the conflicts queued before a deploy — or before the rule existed — wait
/// a week or a click. Best effort: a failure is logged and leaves them to the next sweep.
/// </summary>
public sealed class PersonaAutoMergeStartupSweep : BackgroundService
{
    /// <summary>Lets the API answer its first requests before the sweep takes database connections.</summary>
    private static readonly TimeSpan DefaultStartupDelay = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PersonaAutoMergeStartupSweep> _logger;
    private readonly TimeSpan _startupDelay;

    public PersonaAutoMergeStartupSweep(IServiceScopeFactory scopeFactory, ILogger<PersonaAutoMergeStartupSweep> logger)
        : this(scopeFactory, logger, DefaultStartupDelay)
    {
    }

    internal PersonaAutoMergeStartupSweep(
        IServiceScopeFactory scopeFactory, ILogger<PersonaAutoMergeStartupSweep> logger, TimeSpan startupDelay)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _startupDelay = startupDelay;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(_startupDelay, stoppingToken);
            await using var scope = _scopeFactory.CreateAsyncScope();
            var sweep = scope.ServiceProvider.GetRequiredService<IPersonaAutoMergeService>();
            var result = await sweep.MergeHomonymConflictsAsync(stoppingToken);
            _logger.LogInformation(
                "Startup same-name conflict sweep: {Merged} merged, {Skipped} left in the queue.", result.Merged, result.Skipped);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down before or during the sweep: the next start runs it again.
        }
        catch (Exception ex) when (ex is Microsoft.EntityFrameworkCore.DbUpdateException
                                       or System.Data.Common.DbException
                                       or InvalidOperationException)
        {
            _logger.LogWarning(ex, "The startup same-name conflict sweep failed; the conflicts stay open.");
        }
    }
}
