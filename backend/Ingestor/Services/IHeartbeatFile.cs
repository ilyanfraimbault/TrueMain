namespace Ingestor.Services;

/// <summary>
/// Where the worker's liveness file lives (#1229), resolved once instead of read from the
/// environment on every beat (#1348).
/// </summary>
/// <remarks>
/// This exists as a dependency rather than as a call to
/// <see cref="Environment.GetEnvironmentVariable(string)"/> inside the beat because the
/// environment is process-global while a worker is not. Reading it per beat meant that
/// <em>every</em> <see cref="Worker"/> alive in a process wrote to whatever path the
/// variable happened to hold at that instant — which in the test suite is another test
/// class's heartbeat file, since xUnit runs classes concurrently. That is the shared-state
/// bug behind #1348's "a stopped worker still beats": the extra beat was real, and it came
/// from a different worker.
/// </remarks>
public interface IHeartbeatFile
{
    /// <summary>The path to write, or null when no heartbeat file is configured.</summary>
    string? Path { get; }
}

/// <summary>
/// Production implementation: the path comes from <c>INGESTOR_HEARTBEAT_PATH</c>, read once
/// at construction. Docker sets it for the container's healthcheck; when it is unset the
/// worker keeps no liveness file at all.
/// </summary>
public sealed class EnvironmentHeartbeatFile : IHeartbeatFile
{
    /// <summary>Name of the environment variable Docker sets for the healthcheck.</summary>
    public const string EnvironmentVariable = "INGESTOR_HEARTBEAT_PATH";

    /// <inheritdoc />
    public string? Path { get; } = Environment.GetEnvironmentVariable(EnvironmentVariable) is { } path
        && !string.IsNullOrWhiteSpace(path)
            ? path
            : null;
}
