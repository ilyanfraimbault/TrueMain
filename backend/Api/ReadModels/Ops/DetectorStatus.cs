using System.Text.Json.Serialization;

namespace TrueMain.ReadModels.Ops;

/// <summary>A detector verdict. Ordered by declaration only; precedence lives in
/// <c>DataQualityDetectorEvaluator.Worst</c>.</summary>
public enum DetectorStatus
{
    /// <summary>The measurement could not be taken. Never a pass.</summary>
    [JsonStringEnumMemberName("unknown")]
    Unknown,

    /// <summary>Measured and within its configured band.</summary>
    [JsonStringEnumMemberName("green")]
    Green,

    /// <summary>Measured and past its amber level.</summary>
    [JsonStringEnumMemberName("amber")]
    Amber,

    /// <summary>Measured and past its red level.</summary>
    [JsonStringEnumMemberName("red")]
    Red
}

