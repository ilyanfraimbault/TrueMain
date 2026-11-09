using System.Text.Json.Serialization;

namespace TrueMain.ReadModels.Ops;

/// <summary>What a health alert announces about a key: it started failing, or it recovered.</summary>
public enum HealthAlertChangeKind
{
    [JsonStringEnumMemberName("failing")]
    Failing,

    [JsonStringEnumMemberName("recovered")]
    Recovered
}
