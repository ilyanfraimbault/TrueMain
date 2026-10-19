using System.Text.Json;

namespace TrueMain.Requests.Ops;

/// <summary>Request body for <c>POST /ops/process-runs</c>: an ops script starts a run.</summary>
public sealed record ExternalProcessRunStartRequest
{
    /// <summary>One of the external process names the API accepts (<c>PersonaScraper</c>, <c>OtpSeeder</c>).</summary>
    public string? ProcessName { get; init; }

    /// <summary>The machine the script runs on, shown next to the run.</summary>
    public string? Host { get; init; }

    /// <summary>How the run was started (e.g. <c>weekly</c>, <c>manual</c>), optional.</summary>
    public string? JobMode { get; init; }

    /// <summary>Initial summary (a JSON object), optional.</summary>
    public JsonElement? Summary { get; init; }
}

/// <summary>Request body for <c>POST /ops/process-runs/{id}/heartbeat</c>.</summary>
public sealed record ExternalProcessRunHeartbeatRequest
{
    /// <summary>The run's current summary (a JSON object); omit to only refresh the heartbeat.</summary>
    public JsonElement? Summary { get; init; }
}

/// <summary>Request body for <c>POST /ops/process-runs/{id}/finish</c>.</summary>
public sealed record ExternalProcessRunFinishRequest
{
    /// <summary><c>Success</c>, <c>Failed</c> or <c>Cancelled</c> (case-insensitive).</summary>
    public string? Status { get; init; }

    public string? Error { get; init; }

    /// <summary>The run's final summary (a JSON object); omit to keep the last heartbeat's.</summary>
    public JsonElement? Summary { get; init; }
}
