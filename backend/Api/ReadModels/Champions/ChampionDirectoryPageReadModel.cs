namespace TrueMain.ReadModels.Champions;

/// <summary>
/// One page of the champion directory (<c>GET /champions/directory</c>, #1734): the
/// <c>(champion, position)</c> lines the filters keep, in the requested order, with the
/// filtered total for the pager. Same envelope as <c>LeaderboardResponse</c>, plus the
/// patch the directory was resolved to — carried even on an empty page, so a page that
/// lists nothing still tells the patch picker which patch it is showing.
/// </summary>
public sealed record ChampionDirectoryPageReadModel
{
    public IReadOnlyList<ChampionSummaryReadModel> Rows { get; init; }
        = Array.Empty<ChampionSummaryReadModel>();

    /// <summary>1-indexed current page.</summary>
    public int Page { get; init; }

    /// <summary>Rows per page.</summary>
    public int PageSize { get; init; }

    /// <summary>Lines the filters keep, across every page.</summary>
    public int Total { get; init; }

    /// <summary>The resolved patch; empty when no aggregate data exists yet.</summary>
    public string PatchVersion { get; init; } = string.Empty;
}
