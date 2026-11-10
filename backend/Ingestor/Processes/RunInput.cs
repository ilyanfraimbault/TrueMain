namespace Ingestor.Processes;

/// <summary>
/// What one run of a step covers, when that is narrower than its configuration. A step that
/// cannot be narrowed ignores it; <see cref="Whole"/> is every run of the sequenced lanes.
/// </summary>
/// <remarks>
/// Passed to <see cref="IIngestorProcess.RunCoreAsync"/> rather than set on a scoped service
/// before the process is resolved (#1457 did that), so a run's input is visible at the call
/// and cannot depend on resolution order.
/// </remarks>
public sealed record RunInput
{
    /// <summary>The run its configuration describes: every configured platform, the configured batch.</summary>
    public static RunInput Whole { get; } = new();

    /// <summary>The platforms the run is limited to, or null for every configured one.</summary>
    public IReadOnlyCollection<string>? Platforms { get; private init; }

    /// <summary>The accounts the run claims, or null for the step's configured batch size.</summary>
    public int? BatchSize { get; private init; }

    /// <summary>
    /// A run limited to <paramref name="platforms"/> and <paramref name="batchSize"/> accounts:
    /// one permanent match-fetch worker's batch (#1457).
    /// </summary>
    public static RunInput Narrowed(IReadOnlyCollection<string> platforms, int batchSize)
    {
        ArgumentNullException.ThrowIfNull(platforms);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        return new RunInput { Platforms = platforms, BatchSize = batchSize };
    }
}
