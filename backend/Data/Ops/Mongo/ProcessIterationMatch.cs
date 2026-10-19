using MongoDB.Bson;

namespace Data.Ops.Mongo;

/// <summary>
/// The <c>$match</c> stage that opens the iteration grouping of
/// <see cref="ProcessRunStore.QueryIterationsAsync"/>.
/// </summary>
internal static class ProcessIterationMatch
{
    /// <summary>
    /// Only iteration-stamped runs group into the chain view (<c>$ne: null</c> also excludes
    /// documents without the field). With <paramref name="jobModes"/>, only the runs of passes
    /// opened under one of those modes: every run of a pass carries the pass's mode, so filtering
    /// runs by it keeps or drops whole iterations, never half of one.
    /// </summary>
    public static BsonDocument For(IReadOnlyCollection<string>? jobModes)
    {
        var match = new BsonDocument("iterationId", new BsonDocument("$ne", BsonNull.Value));
        if (jobModes is not null)
        {
            match.Add("jobMode", new BsonDocument("$in", new BsonArray(jobModes)));
        }

        return match;
    }
}
