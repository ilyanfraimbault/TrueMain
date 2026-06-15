namespace TrueMain.UnitTests.Fixtures;

/// <summary>
/// Minimal fake <see cref="TimeProvider"/> that always reports a fixed instant,
/// so time-dependent business logic (claim leases, recency, retention windows)
/// can be frozen under test instead of reading the wall clock (#270).
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
