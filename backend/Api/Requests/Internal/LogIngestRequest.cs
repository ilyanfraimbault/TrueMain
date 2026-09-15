using System.ComponentModel.DataAnnotations;

namespace TrueMain.Requests.Internal;

/// <summary>
/// Request body for <c>POST /internal/logs</c> (#1556): a batch of errors one frontend
/// process observed. Every bound is enforced before anything is written, because the
/// endpoint turns a request body into rows an operator reads.
/// </summary>
public sealed record LogIngestRequest
{
    public const int MaxEntries = 50;

    /// <summary>
    /// Only the two frontends may report; <c>Api</c> and <c>Ingestor</c> rows come from
    /// those hosts' own loggers and cannot be forged through this endpoint.
    /// </summary>
    [Required]
    [AllowedValues("Web", "Admin")]
    public string? Process { get; init; }

    [MaxLength(128)]
    public string? Host { get; init; }

    [Required]
    [MinLength(1)]
    [MaxLength(MaxEntries)]
    public IReadOnlyList<LogIngestEntry>? Entries { get; init; }
}

/// <summary>One reported error, possibly standing for several identical occurrences (<see cref="Count"/>).</summary>
public sealed record LogIngestEntry
{
    /// <summary>When it was first seen, by the frontend's clock; replaced by the server's when implausible.</summary>
    public DateTime? TimestampUtc { get; init; }

    /// <summary>Warning and above only: the ops logs are signal, and so is what is forwarded to them.</summary>
    [Required]
    [AllowedValues("Warning", "Error", "Critical")]
    public string? Level { get; init; }

    [Required]
    [MaxLength(128)]
    public string? Category { get; init; }

    [Required]
    [MaxLength(4_000)]
    public string? Message { get; init; }

    [MaxLength(16_000)]
    public string? Exception { get; init; }

    /// <summary>Checked against <c>OpsEvents.ForwardableEventTypes</c> by the service.</summary>
    [MaxLength(64)]
    public string? EventType { get; init; }

    [MaxLength(16)]
    public string? RequestMethod { get; init; }

    /// <summary>A route template (<c>/api/champions/{n}</c>), not a raw path: the forwarders aggregate on it.</summary>
    [MaxLength(512)]
    public string? RequestPath { get; init; }

    [Range(100, 599)]
    public int? StatusCode { get; init; }

    [Range(0, 3_600_000)]
    public long? DurationMs { get; init; }

    /// <summary>How many identical occurrences this entry stands for since the previous report.</summary>
    [Range(1, 1_000_000)]
    public int? Count { get; init; }
}
