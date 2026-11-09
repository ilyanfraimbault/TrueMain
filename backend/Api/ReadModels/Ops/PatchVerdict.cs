using System.Text.Json.Serialization;

namespace TrueMain.ReadModels.Ops;

/// <summary>Whether the public reads can serve a patch (#1033).</summary>
public enum PatchVerdict
{
    /// <summary>No match and no aggregate row on the patch: nothing to judge.</summary>
    [JsonStringEnumMemberName("unknown")]
    Unknown,

    /// <summary>Enough lines are past the floor for the pages to serve it.</summary>
    [JsonStringEnumMemberName("servable")]
    Servable,

    /// <summary>Aggregated, but too few lines past the floor.</summary>
    [JsonStringEnumMemberName("thin")]
    Thin,

    /// <summary>Matches are ingested, the aggregates have not caught up.</summary>
    [JsonStringEnumMemberName("notAggregated")]
    NotAggregated
}
