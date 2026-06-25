namespace TrueMain.ReadModels.Truemains;

/// <summary>
/// Response for the truemain search endpoint (<c>GET /truemains/search</c>):
/// a short, ranked list of accounts whose <c>GameName</c> matches the query,
/// used to power the name/tag lookup that links straight to a profile page.
/// The population mirrors the leaderboard's (ranked mains on the exposed
/// regions) so search is a faster way into the same list, not a different one.
/// </summary>
public sealed record SearchResponse
{
    public IReadOnlyList<SearchResultReadModel> Results { get; init; }
        = Array.Empty<SearchResultReadModel>();
}

public sealed record SearchResultReadModel
{
    public ProfileIdentityReadModel Identity { get; init; } = new();

    /// <summary>One of <c>europe</c>, <c>americas</c>, <c>korea</c>. Mirrors the leaderboard's region slug.</summary>
    public string Region { get; init; } = string.Empty;

    /// <summary>
    /// Latest ranked cell (tier/division/LP) for the account, so the dropdown
    /// can show rank next to the name. Null when the account has no rank
    /// snapshot yet — which, given the ranked gate on the population, only
    /// happens transiently between discovery and the first rank sync.
    /// </summary>
    public SearchRankedReadModel? Ranked { get; init; }
}

public sealed record SearchRankedReadModel
{
    public string Tier { get; init; } = string.Empty;

    public string Division { get; init; } = string.Empty;

    public int LeaguePoints { get; init; }
}
