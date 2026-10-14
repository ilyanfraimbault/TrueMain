using TrueMain.ReadModels.Personas;

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

    /// <summary>
    /// Known players whose name matches the term (#2018), best first, at most five. Empty when the
    /// query carries a tag — a <c>Name#TAG</c> asks for one account, not a person.
    /// </summary>
    public IReadOnlyList<SearchPlayerReadModel> Players { get; init; }
        = Array.Empty<SearchPlayerReadModel>();
}

/// <summary>A known player in the search results, linking to <c>/players/{slug}</c>.</summary>
public sealed record SearchPlayerReadModel
{
    public PersonaTagReadModel Persona { get; init; } = new();

    /// <summary>The person's tracked accounts — the ones their page lists. Never 0: a person with none is not returned.</summary>
    public int AccountCount { get; init; }

    /// <summary>The best-ranked tracked account's icon.</summary>
    public int? ProfileIconId { get; init; }

    /// <summary>The best-ranked tracked account's rank; null when it is unranked.</summary>
    public SearchRankedReadModel? Ranked { get; init; }
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

    /// <summary>
    /// Up to 3 most-played champion ids (descending play rate), same slice
    /// the leaderboard row shows, so the dropdown can render the player's
    /// truemained champions at a glance. Empty when no main-champion analysis
    /// has run yet.
    /// </summary>
    public IReadOnlyList<int> TopChampionIds { get; init; } = Array.Empty<int>();

    /// <summary>
    /// Primary + secondary lane from position share across the player's mains
    /// (same derivation as the leaderboard, via <c>MainPositions</c>). Null
    /// when no main-champion analysis has run.
    /// </summary>
    public LeaderboardPositionsReadModel? Positions { get; init; }
}

public sealed record SearchRankedReadModel
{
    public string Tier { get; init; } = string.Empty;

    public string Division { get; init; } = string.Empty;

    public int LeaguePoints { get; init; }
}
