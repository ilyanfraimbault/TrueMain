using Ingestor.Services;

namespace TrueMain.UnitTests;

/// <summary>
/// A worker that keeps no liveness file. Every <c>Worker</c> test other than
/// <see cref="WorkerHeartbeatTests"/> uses it, which is the point: the heartbeat path used
/// to come from a process-global environment variable read on every beat, so those workers
/// wrote into whichever file the heartbeat test had configured and made its shutdown
/// assertion fail from another collection running concurrently (#1348).
/// </summary>
internal sealed class NoHeartbeatFile : IHeartbeatFile
{
    public static NoHeartbeatFile Instance { get; } = new();

    public string? Path => null;
}
