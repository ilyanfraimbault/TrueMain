using System.Text.Json.Serialization;
namespace TrueMain.ReadModels.Champions;

/// <summary>
/// A champion worth banning, and why (#1906).
/// </summary>
public sealed record DraftBanCandidateReadModel
{
    public int ChampionId { get; init; }

    /// <summary>
    /// The ranking key: how far behind our pick (or pool) is into this champion,
    /// shrunk by its games, times how often it is played on our lane. Zero for a
    /// suggestion from the ban-rate fallback. Not a win probability and not meant
    /// to be displayed.
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// The facts behind the suggestion, strongest first: <c>laneThreat</c> (our
    /// pick is behind into it) or <c>banRate</c> (the fallback: how often it is
    /// banned) — see <see cref="DraftReasonKind"/>.
    /// </summary>
    public IReadOnlyList<DraftReasonReadModel> Reasons { get; init; } = [];
}

/// <summary>What the ban suggestions protect.</summary>
public enum DraftBanTarget
{
    /// <summary>The player declared a pick (their hover in the planning phase).</summary>
    [JsonStringEnumMemberName("pick")]
    Pick,

    /// <summary>No declared pick: the player's pool, weighted by mastery.</summary>
    [JsonStringEnumMemberName("pool")]
    Pool,

    /// <summary>Nothing to protect: the lane's most banned champions, labelled as such.</summary>
    [JsonStringEnumMemberName("none")]
    None,
}

/// <summary>The ban suggestions for one champion-select state.</summary>
public sealed record DraftBanResponse
{
    public string Position { get; init; } = string.Empty;

    public string? Patch { get; init; }

    public string? PreviousPatch { get; init; }

    public string EloBracket { get; init; } = string.Empty;

    public DraftBanTarget Target { get; init; } = DraftBanTarget.None;

    /// <summary>The champions protected: the declared pick, or the pool members weighed.</summary>
    public IReadOnlyList<int> TargetChampionIds { get; init; } = [];

    /// <summary>Suggestions, best first.</summary>
    public IReadOnlyList<DraftBanCandidateReadModel> Candidates { get; init; } = [];
}
