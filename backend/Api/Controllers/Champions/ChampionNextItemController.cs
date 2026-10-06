using Microsoft.AspNetCore.Mvc;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.NextItem;

namespace TrueMain.Controllers.Champions;

/// <summary>
/// The next item to complete in a live game (#1749), read by the desktop app's in-game panel.
/// </summary>
/// <remarks>
/// A POST for the same reason as the draft endpoint: the request is a whole game state — ten
/// champions, their lanes, the items held — which does not fit a query string and changes on
/// every purchase.
/// </remarks>
public sealed class ChampionNextItemController(INextItemQueryService nextItem) : ChampionsControllerBase
{
    private const int MaxAllies = 4;
    private const int MaxEnemies = 5;

    /// <summary>An inventory is seven slots; a purchase history longer than this is not a League game.</summary>
    private const int MaxItems = 30;

    /// <summary>
    /// Rank the items this game's champion could complete next, from where its build stands
    /// and against the game it is in.
    /// </summary>
    [HttpPost("{championId:int}/next-item")]
    [ProducesResponseType(typeof(ChampionNextItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ChampionNextItemResponse>> PostAsync(
        int championId,
        [FromBody] NextItemRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var position = Lane(request.Position);
        if (position is null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Unknown position",
                Detail = "Position must be one of TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        var criteria = new NextItemCriteria
        {
            Position = position,
            Patch = request.Patch,
            Items = (request.Items ?? []).Take(MaxItems).ToList(),
            Allies = Participants(request.Allies, MaxAllies),
            Enemies = Participants(request.Enemies, MaxEnemies),
            GoldLeadAt15 = request.GoldLeadAt15,
        };

        return Ok(await nextItem.GetAsync(championId, criteria, ct));
    }

    private static string? Lane(string? raw) => Core.Lol.Map.LanePositions.Normalize(raw);

    /// <summary>A participant whose lane is not a lane keeps the champion and loses the lane, rather than failing the request.</summary>
    private static List<NextItemParticipant> Participants(IReadOnlyList<NextItemRequestParticipant>? raw, int max)
        => [.. (raw ?? [])
            .Where(participant => participant.ChampionId > 0)
            .DistinctBy(participant => participant.ChampionId)
            .Take(max)
            .Select(participant => new NextItemParticipant(participant.ChampionId, Lane(participant.Position)))];

    /// <summary>The game state, as posted by the client.</summary>
    public sealed record NextItemRequest
    {
        /// <summary>The player's own lane.</summary>
        public string? Position { get; init; }

        /// <summary>Optional; the newest patch the champion has a model for when omitted.</summary>
        public string? Patch { get; init; }

        /// <summary>Items held or completed, in completion order when known.</summary>
        public IReadOnlyList<int>? Items { get; init; }

        public IReadOnlyList<NextItemRequestParticipant>? Allies { get; init; }

        public IReadOnlyList<NextItemRequestParticipant>? Enemies { get; init; }

        /// <summary>Our gold lead over the lane opponent at 15:00, once the game is past it.</summary>
        public double? GoldLeadAt15 { get; init; }
    }

    /// <summary>Another participant: a champion and, when known, its lane.</summary>
    public sealed record NextItemRequestParticipant
    {
        public int ChampionId { get; init; }

        public string? Position { get; init; }
    }
}
