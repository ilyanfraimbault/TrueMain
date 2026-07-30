using TrueMain.ReadModels.Truemains;

namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Response of <c>POST /champions/{id}/composition-build/games</c>: the games
/// the recommendation was actually computed from, in selection order, one page
/// at a time (#940). Separate from the recommendation payload on purpose — the
/// matchup page refetches the build on every draft edit, and hydrating a
/// hundred match rows on each keystroke for a panel most visits never open
/// would be paid by everyone.
/// </summary>
public sealed record CompositionBuildGamesResponse
{
    public required int ChampionId { get; init; }

    public required string Position { get; init; }

    /// <summary>Normalised patch filter applied, null when unfiltered.</summary>
    public string? Patch { get; init; }

    /// <summary>1-indexed page returned (after clamping).</summary>
    public required int Page { get; init; }

    /// <summary>Page size the server actually used (after clamping).</summary>
    public required int PageSize { get; init; }

    /// <summary>Selected games across all pages — the recommendation's sample.</summary>
    public required int Total { get; init; }

    /// <summary>
    /// Score a game reproducing every requested slot would reach; the
    /// denominator of each game's <see cref="CompositionGameReadModel.Score"/>.
    /// Zero when the draft carried no composition slot — every game then
    /// scores 0 and the ratio is undefined rather than 0%.
    /// </summary>
    public required int MaxPossibleScore { get; init; }

    /// <summary>
    /// The page's games, in the selection's own order: games piloted by a main
    /// of the champion first, then best similarity, recency breaking ties.
    /// </summary>
    public required IReadOnlyList<CompositionGameReadModel> Games { get; init; }
}

/// <summary>
/// One sampled game: the collapsed match row, its similarity score, and who
/// piloted it — the two things the aggregation weighed it on.
/// </summary>
public sealed record CompositionGameReadModel
{
    /// <summary>Similarity score of the game, out of <c>MaxPossibleScore</c>.</summary>
    public required int Score { get; init; }

    /// <summary>True when the pilot is an active main of the champion.</summary>
    public required bool IsTruemain { get; init; }

    /// <summary>
    /// The pilot's Riot identity, null when the game's participant carries no
    /// resolved Riot account (harvested rows can pre-date account resolution).
    /// </summary>
    public CompositionGamePilotReadModel? Pilot { get; init; }

    /// <summary>The pilot's slice of the game, in the match-feed row shape.</summary>
    public required MatchSummaryReadModel Match { get; init; }
}

/// <summary>Riot identity of a sampled game's pilot.</summary>
public sealed record CompositionGamePilotReadModel
{
    public required string GameName { get; init; }

    public string? TagLine { get; init; }

    /// <summary>Riot profile icon id, 0 when never resolved.</summary>
    public required int ProfileIconId { get; init; }
}
