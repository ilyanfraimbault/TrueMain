using System.Text.Json;
using Data.Entities;
using Data.Ops.Mongo;
using TrueMain.ReadModels.Ops;
using TrueMain.Requests.Ops;

namespace TrueMain.Services.Ops.Processes;

public interface IExternalProcessRunService
{
    Task<ExternalProcessRunOutcome> StartAsync(ExternalProcessRunStartRequest request, CancellationToken ct);

    Task<ExternalProcessRunOutcome> HeartbeatAsync(Guid id, ExternalProcessRunHeartbeatRequest request, CancellationToken ct);

    Task<ExternalProcessRunOutcome> FinishAsync(Guid id, ExternalProcessRunFinishRequest request, CancellationToken ct);
}

/// <summary>What happened to an external-run write; the controller maps it to a status code.</summary>
public enum ExternalProcessRunResult
{
    Ok,
    Invalid,
    NotFound,
    NotRunning,
}

public sealed record ExternalProcessRunOutcome(
    ExternalProcessRunResult Result,
    string? Error = null,
    ExternalProcessRunStartedResponse? Started = null);

/// <summary>
/// The write side of the ops scripts' runs (#2119). They land in <c>process_runs</c> next to the
/// Ingestor's, so the admin Processes page lists, filters, details and ages them out to
/// Abandoned (heartbeat older than <see cref="ProcessRunStaleness.Threshold"/>) the same way.
/// Only the names in <see cref="ExternalProcessNames"/> are accepted, and only a run of one of
/// them can be heartbeaten or finished: the ops key must never forge or close an Ingestor run.
/// </summary>
public sealed class ExternalProcessRunService(
    IProcessRunStore runStore,
    IExternalProcessRunStore externalRunStore,
    TimeProvider timeProvider) : IExternalProcessRunService
{
    /// <summary>The ops scripts that report their runs, kept outside the repo.</summary>
    public static readonly IReadOnlyList<string> ExternalProcessNames = ["PersonaScraper", "OtpSeeder"];

    /// <summary>
    /// Upper bound on a stored summary. A summary is a run report (steps, counters, a few error
    /// lines), not a log: 64 KiB is ample and keeps a runaway script from bloating the collection.
    /// </summary>
    public const int MaxSummaryBytes = 64 * 1024;

    private const int MaxErrorLength = 2048;
    private const int MaxLabelLength = 128;

    private static readonly ProcessRunStatus[] FinishStatuses =
        [ProcessRunStatus.Success, ProcessRunStatus.Failed, ProcessRunStatus.Cancelled];

    public async Task<ExternalProcessRunOutcome> StartAsync(ExternalProcessRunStartRequest request, CancellationToken ct)
    {
        var name = ExternalProcessNames.FirstOrDefault(n =>
            string.Equals(n, request.ProcessName?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return Invalid($"processName must be one of: {string.Join(", ", ExternalProcessNames)}.");
        }

        if (!TrySerializeSummary(request.Summary, out var summaryJson, out var summaryError))
        {
            return Invalid(summaryError!);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var run = new ProcessRunDocument
        {
            Id = Guid.NewGuid(),
            ProcessName = name,
            JobMode = Label(request.JobMode),
            StartedAtUtc = now,
            FinishedAtUtc = now,
            Status = ProcessRunStatus.Running,
            Host = Label(request.Host),
            LastHeartbeatAtUtc = now,
            SummaryJson = summaryJson,
        };
        await runStore.InsertAsync(run, ct);
        return new ExternalProcessRunOutcome(
            ExternalProcessRunResult.Ok,
            Started: new ExternalProcessRunStartedResponse { Id = run.Id, ProcessName = name, StartedAtUtc = now });
    }

    public async Task<ExternalProcessRunOutcome> HeartbeatAsync(
        Guid id, ExternalProcessRunHeartbeatRequest request, CancellationToken ct)
    {
        if (!TrySerializeSummary(request.Summary, out var summaryJson, out var summaryError))
        {
            return Invalid(summaryError!);
        }

        var run = await FindExternalAsync(id, ct);
        if (run is null)
        {
            return new ExternalProcessRunOutcome(ExternalProcessRunResult.NotFound);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        return await externalRunStore.HeartbeatAsync(id, now, summaryJson, ct)
            ? new ExternalProcessRunOutcome(ExternalProcessRunResult.Ok)
            : NotRunning(run);
    }

    public async Task<ExternalProcessRunOutcome> FinishAsync(
        Guid id, ExternalProcessRunFinishRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<ProcessRunStatus>(request.Status?.Trim(), ignoreCase: true, out var status)
            || !FinishStatuses.Contains(status))
        {
            return Invalid($"status must be one of: {string.Join(", ", FinishStatuses)}.");
        }

        if (!TrySerializeSummary(request.Summary, out var summaryJson, out var summaryError))
        {
            return Invalid(summaryError!);
        }

        var run = await FindExternalAsync(id, ct);
        if (run is null)
        {
            return new ExternalProcessRunOutcome(ExternalProcessRunResult.NotFound);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var durationMs = (int)Math.Clamp((now - run.StartedAtUtc).TotalMilliseconds, 0, int.MaxValue);
        var error = string.IsNullOrWhiteSpace(request.Error) ? null : Clamp(request.Error.Trim(), MaxErrorLength);
        return await externalRunStore.FinishAsync(id, now, durationMs, status, error, summaryJson, ct)
            ? new ExternalProcessRunOutcome(ExternalProcessRunResult.Ok)
            : NotRunning(run);
    }

    private async Task<ProcessRunDocument?> FindExternalAsync(Guid id, CancellationToken ct)
    {
        var run = await externalRunStore.GetAsync(id, ct);
        return run is not null && ExternalProcessNames.Contains(run.ProcessName) ? run : null;
    }

    /// <summary>A summary must be a JSON object within <see cref="MaxSummaryBytes"/>; absent is fine.</summary>
    internal static bool TrySerializeSummary(JsonElement? summary, out string? json, out string? error)
    {
        json = null;
        error = null;
        if (summary is not { } element || element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            error = "summary must be a JSON object.";
            return false;
        }

        var text = element.GetRawText();
        if (System.Text.Encoding.UTF8.GetByteCount(text) > MaxSummaryBytes)
        {
            error = $"summary is larger than {MaxSummaryBytes / 1024} KiB; send counters, not logs.";
            return false;
        }

        json = text;
        return true;
    }

    private static ExternalProcessRunOutcome Invalid(string error) => new(ExternalProcessRunResult.Invalid, error);

    private static ExternalProcessRunOutcome NotRunning(ProcessRunDocument run) =>
        new(ExternalProcessRunResult.NotRunning, $"run {run.Id} is {run.Status}, not Running.");

    private static string? Label(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Clamp(value.Trim(), MaxLabelLength);

    private static string Clamp(string value, int max) => value.Length <= max ? value : value[..max];
}
