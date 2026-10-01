using System.ComponentModel.DataAnnotations;

namespace TrueMain.Controllers.Champions;

/// <summary>
/// Query parameters for <c>GET /champions/directory</c> (#1734). <see cref="Page"/>,
/// <see cref="PageSize"/> and <see cref="ChampionId"/> carry <see cref="RangeAttribute"/>s
/// so <c>[ApiController]</c> rejects an out-of-range value with a 400 at binding time, the
/// same contract as <c>GET /truemains</c>. <see cref="Sort"/> and <see cref="Order"/> are
/// presentation preferences: an unknown value falls back to the default order instead of
/// failing the request.
/// </summary>
public sealed record ChampionDirectoryQuery
{
    public string? Patch { get; init; }

    public string? EloBracket { get; init; }

    /// <summary>Mains only (the default) or every tracked player with games on the champion.</summary>
    public bool? TruemainsOnly { get; init; }

    public string? Position { get; init; }

    [Range(1, int.MaxValue)]
    public int? ChampionId { get; init; }

    /// <summary><c>pickRate</c> (default), <c>winRate</c>, <c>banRate</c>, <c>games</c> or <c>tier</c>.</summary>
    public string? Sort { get; init; }

    /// <summary><c>desc</c> (default, strongest first) or <c>asc</c>.</summary>
    public string? Order { get; init; }

    [Range(1, int.MaxValue)]
    public int? Page { get; init; }

    [Range(1, 100)]
    public int? PageSize { get; init; }
}
