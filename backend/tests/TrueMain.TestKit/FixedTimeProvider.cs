namespace TrueMain.TestKit;

/// <summary>
/// Minimal fake <see cref="TimeProvider"/> that always reports a fixed instant,
/// so time-dependent business logic (claim leases, recency, retention windows)
/// can be frozen under test instead of reading the wall clock (#270).
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
