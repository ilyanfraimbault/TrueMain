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
public sealed class ChampionDraftController(
    IDraftRecommendationQueryService draft,
    IDraftBanQueryService bans)
    : ChampionsControllerBase
{
    /// <summary>Maximum candidates scored in one request.</summary>
    /// <remarks>
    /// A player's pool, not the whole roster. The ceiling bounds the synergy
    /// work, which is one query per locked ally over this set.
    /// </remarks>
    private const int MaxCandidates = 40;

    /// <summary>
    /// A side has five cells. The solver enumerates placements into five lanes
    /// and has none to offer a sixth champion, so a client sending more is cut
    /// to the first five rather than answered with a 500.
    /// </summary>
    private const int MaxEnemies = 5;

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
        var position = Core.Lol.Map.LanePositions.Normalize(request.Position);
        if (position is null)
        {
            return UnknownPosition();
        }

        var criteria = new DraftCriteria
        {
            Position = position,
            EnemyChampions = (request.EnemyChampions ?? []).Distinct().Take(MaxEnemies).ToList(),
            PinnedEnemyLanes = Normalize(request.PinnedEnemyLanes),
            PreviousEnemyLanes = request.PreviousEnemyLanes is null
                ? null
                : Normalize(request.PreviousEnemyLanes),
            Allies = ByLane(request.Allies),
            HoveredAllies = ByLane(request.HoveredAllies),
            Bans = request.Bans ?? [],
            Candidates = (request.Candidates ?? []).Take(MaxCandidates).ToList(),
            Patch = request.Patch,
            EloBracket = request.EloBracket,
        };

        return Ok(await draft.GetAsync(criteria, ct));
    }

    /// <summary>
    /// Suggest the bans for one draft: the threats to the player's declared pick,
    /// or to their pool, never a champion an ally is playing (#1906).
    /// </summary>
    [HttpPost("draft/bans")]
    [ProducesResponseType(typeof(DraftBanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DraftBanResponse>> PostBansAsync(
        [FromBody] DraftBanRequest request,
        CancellationToken ct = default)
    {
        var position = Core.Lol.Map.LanePositions.Normalize(request.Position);
        if (position is null)
        {
            return UnknownPosition();
        }

        var criteria = new DraftBanCriteria
        {
            Position = position,
            PlannedPick = request.PlannedPick,
            Pool = (request.Pool ?? [])
                .Take(MaxCandidates)
                .Select(entry => new DraftPoolEntry(entry.ChampionId, entry.Weight))
                .ToList(),
            AllyChampions = request.AllyChampions ?? [],
            EnemyChampions = request.EnemyChampions ?? [],
            Bans = request.Bans ?? [],
            Patch = request.Patch,
            EloBracket = request.EloBracket,
        };

        return Ok(await bans.GetAsync(criteria, ct));
    }

    private BadRequestObjectResult UnknownPosition()
        => BadRequest(new ProblemDetails
        {
            Title = "Unknown position",
            Detail = "Position must be one of TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY.",
            Status = StatusCodes.Status400BadRequest,
        });

    private static Dictionary<string, int> ByLane(IReadOnlyDictionary<string, int>? lanes)
        => (lanes ?? new Dictionary<string, int>())
            .ToDictionary(e => e.Key.ToUpperInvariant(), e => e.Value, StringComparer.Ordinal);

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

        /// <summary>Allies hovering a champion they have not locked, keyed by lane (#1906).</summary>
        public IReadOnlyDictionary<string, int>? HoveredAllies { get; init; }

        public IReadOnlyList<int>? Bans { get; init; }

        /// <summary>The player's champion pool, to rank.</summary>
        public IReadOnlyList<int>? Candidates { get; init; }

        public string? Patch { get; init; }

        public string? EloBracket { get; init; }
    }

    /// <summary>The champion-select state the ban suggestions need, as posted by the client.</summary>
    public sealed record DraftBanRequest
    {
        /// <summary>The player's own lane.</summary>
        public string? Position { get; init; }

        /// <summary>The pick the player declared (their own hover), if any.</summary>
        public int? PlannedPick { get; init; }

        /// <summary>The player's pool with its mastery points, protected when no pick is declared.</summary>
        public IReadOnlyList<PoolEntry>? Pool { get; init; }

        /// <summary>Allies' champions, locked or hovered — never suggested.</summary>
        public IReadOnlyList<int>? AllyChampions { get; init; }

        /// <summary>Enemy champions already picked.</summary>
        public IReadOnlyList<int>? EnemyChampions { get; init; }

        public IReadOnlyList<int>? Bans { get; init; }

        public string? Patch { get; init; }

        public string? EloBracket { get; init; }
    }

    /// <summary>One champion of the player's pool.</summary>
    public sealed record PoolEntry
    {
        public int ChampionId { get; init; }

        /// <summary>Mastery points, or any non-negative weight.</summary>
        public double Weight { get; init; }
    }
}
