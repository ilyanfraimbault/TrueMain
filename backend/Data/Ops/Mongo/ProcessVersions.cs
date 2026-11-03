namespace Data.Ops.Mongo;

/// <summary>What counts as a deploy between two boots of one process (#2297).</summary>
public static class ProcessVersions
{
    /// <summary>
    /// Whether a boot on <paramref name="current"/> is a deploy after one on
    /// <paramref name="previous"/>: a different version, or the first versioned boot on record.
    /// A boot without a version is never one. Ordinal: a version is an identifier, and
    /// <c>1.2.0-RC.1</c> is not <c>1.2.0-rc.1</c>.
    /// </summary>
    public static bool IsVersionChange(string? previous, string? current)
        => current is not null && !string.Equals(previous, current, StringComparison.Ordinal);
}
