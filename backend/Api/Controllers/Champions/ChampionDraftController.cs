using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Draft;

namespace TrueMain.Controllers.Champions;

/// <summary>
/// The draft assistant the desktop companion app reads (#1675).
/// </summary>
/// <remarks>
/// A POST rather than a GET: the request carries a whole champion-select state
/// — two teams, bans, pinned lanes, a candidate pool — which does not fit a
/// query string, and it changes on every pick so there is nothing to bookmark.
/// </remarks>
public sealed class ChampionDraftController(IDraftRecommendationQueryService draft)
    : ChampionsControllerBase
{
    /// <summary>Maximum candidates scored in one request.</summary>
    /// <remarks>
    /// A player's pool, not the whole roster. The ceiling bounds the synergy
    /// work, which is one query per locked ally over this set.
    /// </remarks>
    private const int MaxCandidates = 40;

    /// <summary>
    /// Resolve the enemy lanes and rank the candidate picks for one draft.
    /// </summary>
    [HttpPost("draft")]
    [ProducesResponseType(typeof(DraftRecommendationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DraftRecommendationResponse>> PostAsync(
        [FromBody] DraftRequest request,
        CancellationToken ct = default)
    {
        var position = (request.Position ?? string.Empty).Trim().ToUpperInvariant();
        if (!Core.Lol.Map.QueueDataQualityProfile.LanePositions.Contains(position))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Unknown position",
                Detail = "Position must be one of TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        var criteria = new DraftCriteria
        {
            Position = position,
            EnemyChampions = request.EnemyChampions ?? [],
            PinnedEnemyLanes = Normalize(request.PinnedEnemyLanes),
            PreviousEnemyLanes = request.PreviousEnemyLanes is null
                ? null
                : Normalize(request.PreviousEnemyLanes),
            Allies = (request.Allies ?? new Dictionary<string, int>())
                .ToDictionary(e => e.Key.ToUpperInvariant(), e => e.Value, StringComparer.Ordinal),
            Bans = request.Bans ?? [],
            Candidates = (request.Candidates ?? []).Take(MaxCandidates).ToList(),
            Patch = request.Patch,
            EloBracket = request.EloBracket,
        };

        return Ok(await draft.GetAsync(criteria, ct));
    }

    private static Dictionary<int, string> Normalize(IReadOnlyDictionary<int, string>? lanes)
        => (lanes ?? new Dictionary<int, string>())
            .ToDictionary(e => e.Key, e => e.Value.ToUpperInvariant());

    /// <summary>The champion-select state, as posted by the client.</summary>
    public sealed record DraftRequest
    {
        /// <summary>The player's own lane.</summary>
        public string? Position { get; init; }

        /// <summary>Enemy champions picked or hovered so far.</summary>
        public IReadOnlyList<int>? EnemyChampions { get; init; }

        /// <summary>Lanes the user corrected by hand, by enemy champion id.</summary>
        public IReadOnlyDictionary<int, string>? PinnedEnemyLanes { get; init; }

        /// <summary>
        /// The placement currently on screen, so an equally-good re-solve does
        /// not reshuffle the panel between two picks.
        /// </summary>
        public IReadOnlyDictionary<int, string>? PreviousEnemyLanes { get; init; }

        /// <summary>Allies already locked, keyed by lane.</summary>
        public IReadOnlyDictionary<string, int>? Allies { get; init; }

        public IReadOnlyList<int>? Bans { get; init; }

        /// <summary>The player's champion pool, to rank.</summary>
        public IReadOnlyList<int>? Candidates { get; init; }

        public string? Patch { get; init; }

        public string? EloBracket { get; init; }
    }
}
