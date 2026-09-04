namespace TrueMain.TestKit;

/// <summary>
/// Clock helpers for tests whose <em>meaning</em> depends on the UTC calendar day rather
/// than on elapsed time.
/// </summary>
public static class TestInstants
{
    /// <summary>
    /// <paramref name="ago"/> before now, but never earlier than the start of today in UTC.
    /// </summary>
    /// <remarks>
    /// For the rank snapshots this exists for, "an hour ago" is not the point — "the same
    /// UTC day" is: <c>rank_snapshots</c> carries one row per account per UTC day, so a
    /// seeded snapshot only exercises the update-in-place branch while it shares today's
    /// date. A plain <c>UtcNow.AddHours(-1)</c> silently crosses into yesterday for the
    /// first hour after midnight, which flips the writer to its insert branch and fails
    /// the test with a duplicate row — reproducibly, and only between 00:00 and 01:00 UTC,
    /// which is exactly the shape of bug that reads as a random flake.
    /// </remarks>
    /// <param name="ago">How far back to go, before the clamp.</param>
    /// <param name="nowUtc">
    /// The instant to measure from. Defaults to the wall clock; the tests that pin the
    /// midnight boundary pass it explicitly, since the branch it guards is reachable one
    /// hour a day and would otherwise be asserted only by luck.
    /// </param>
    public static DateTime EarlierSameUtcDay(TimeSpan ago, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var earlier = now - ago;
        return earlier < now.Date ? now.Date : earlier;
    }
}
