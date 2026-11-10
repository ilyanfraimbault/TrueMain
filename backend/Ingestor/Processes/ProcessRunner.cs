using Data.Pipeline;
using Ingestor.Processes.Summaries;
using Ingestor.Services;

namespace Ingestor.Processes;

/// <summary>
/// Runs one pipeline step: the harness the sequenced lanes (<see cref="Worker"/>) and the
/// permanent match-fetch workers share, so a run is set up and accounted for the same way
/// wherever it starts.
/// </summary>
/// <remarks>
/// <para>
/// Each run gets a fresh DI scope — its own DbContext, nothing cached from the previous step
/// (#256) — and the process is resolved from that scope by its <see cref="JobMode"/>. While it
/// runs, the ambient caller context names it, so every Riot call it makes is attributed and
/// budgeted to it (#1035). The run itself is recorded by <see cref="RecordedProcess{TInner}"/>.
/// </para>
/// <para>
/// A failure is logged and counted (#260) and returned as <see cref="StepRun.Failed"/>: one
/// failing step must not stop the steps after it (#443) nor end a worker. A shutdown is not a
/// failure (#255) and propagates untouched. A step with no registered process is a wiring
/// fault, thrown before anything runs.
/// </para>
/// </remarks>
public sealed class ProcessRunner(
    IServiceScopeFactory scopeFactory,
    ICallerContext callerContext,
    IngestorMetrics metrics,
    ILogger<ProcessRunner> logger)
{
    /// <summary>
    /// Runs <paramref name="step"/> over <paramref name="input"/> for a host running
    /// <paramref name="hostMode"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">No process is registered for the step.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public async Task<StepRun> RunAsync(PipelineProcess step, JobMode hostMode, RunInput input, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        // Resolve from THIS scope's provider (never the root one, #256), so the process and
        // its scoped dependencies die with the scope. GetRequiredKeyedService names only the
        // key type in its message, hence the lenient resolve and a throw naming the step.
        var process = scope.ServiceProvider.GetKeyedService<IIngestorProcess>(step.Mode)
            ?? throw new InvalidOperationException(
                $"No IIngestorProcess is registered for {nameof(JobMode)}.{step.Mode} "
                + $"(reached while running Job:Mode '{hostMode}'). "
                + $"Registered modes: {DescribeRegisteredModes(scope.ServiceProvider)}. "
                + "Mark its class with [PipelineStep] so AddIngestorProcesses registers it.");

        using var caller = callerContext.BeginCall(step.Name);

        try
        {
            return StepRun.Completed(await process.RunCoreAsync(input, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The decorator has already recorded the Failed run. A log line alone is not
            // alertable, so the failure is counted too — per step, which is where a dead Riot
            // key or schema drift surfaces (#260).
            logger.LogError(ex, "Process {ProcessName} failed (Job:Mode {Mode}).", step.Name, hostMode);
            metrics.RecordRunFailure(step.Name, hostMode);
            return StepRun.Failed(ex);
        }
    }

    private static string DescribeRegisteredModes(IServiceProvider serviceProvider)
    {
        // IServiceProviderIsKeyedService answers "is this key registered?" without constructing
        // anything; enumerating the keyed services instead would build every process (each with
        // its DbContext) just to format this message, and could throw and mask the real fault.
        var probe = serviceProvider.GetService<IServiceProviderIsKeyedService>();
        if (probe is null)
        {
            return "unavailable (this container cannot be probed for keyed registrations)";
        }

        var registered = Enum.GetValues<JobMode>()
            .Where(candidate => probe.IsKeyedService(typeof(IIngestorProcess), candidate))
            .ToArray();

        return registered.Length == 0 ? "none" : string.Join(", ", registered);
    }
}

/// <summary>How one run of a step ended, for the loop that started it.</summary>
/// <param name="Summary">What the step reported, when it completed.</param>
/// <param name="Error">Why it failed, when it did.</param>
public sealed record StepRun(IProcessRunSummary? Summary, Exception? Error)
{
    /// <summary>Whether the step threw.</summary>
    public bool IsFailed => Error is not null;

    /// <summary>A run that completed with <paramref name="summary"/>.</summary>
    public static StepRun Completed(IProcessRunSummary? summary) => new(summary, null);

    /// <summary>A run that threw <paramref name="error"/>.</summary>
    public static StepRun Failed(Exception error) => new(null, error);
}
