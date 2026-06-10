using Data.Logging.Mongo;

namespace TrueMain.TestKit;

/// <summary>
/// No-op <see cref="IAuditLog"/> for tests that drive an operator write path but
/// do not assert on the audit trail. Records nothing and reports disabled, so the
/// production code's "audit store not configured ⇒ no-op" branch is exercised.
/// </summary>
public sealed class NoOpAuditLog : IAuditLog
{
    public bool IsEnabled => false;

    public Task RecordAsync(
        string action,
        string actor,
        string? targetType = null,
        string? targetId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
        => Task.CompletedTask;
}
