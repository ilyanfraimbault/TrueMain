namespace Data.Entities;

/// <summary>
/// The canonical minute marks a <see cref="MatchParticipantTimelineSnapshot"/> is captured at.
/// </summary>
/// <remarks>
/// A two-way invariant, which is why it lives once, next to the table: the ingestor writes
/// a snapshot only at these marks, match-data retention deletes every snapshot that is not
/// at one of them (#772), and the performance score iterates this list rather than the rows
/// that exist. Dropping a mark here therefore drops it everywhere at once — removing it on
/// one side only would make a lead disappear with no error, just a slightly different score.
/// An array rather than a read-only list because retention filters on it inside an EF Core
/// query, which translates an array to <c>= ANY(@p)</c>.
/// </remarks>
public static class TimelineSnapshotMarks
{
    /// <summary>The canonical marks, in minutes, in ascending order.</summary>
    public static readonly int[] Minutes = [5, 10, 15, 20, 30];
}
