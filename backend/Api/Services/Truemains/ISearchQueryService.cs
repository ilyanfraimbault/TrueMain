using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

public interface ISearchQueryService
{
    /// <summary>
    /// Looks up truemains by Riot id. The query is matched case-insensitively
    /// against <c>GameName</c> as a substring (trigram index), optionally
    /// narrowed by a tag line when the caller passes <c>Name#TAG</c>. Results
    /// are capped at <paramref name="limit"/> and ordered so an exact name
    /// match, then the highest-ranked accounts, surface first. Returns an empty
    /// list when the query is too short to search or nothing matches.
    /// </summary>
    Task<SearchResponse> SearchAsync(string? query, int limit, CancellationToken ct);
}
