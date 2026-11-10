using Core.Lol.Map;
using TrueMain.Services.Champions.NextItem;

namespace TrueMain.Requests.Champions;

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

/// <summary>Another participant: a champion and, when known, its lane and what it holds.</summary>
public sealed record NextItemRequestParticipant
{
    public int ChampionId { get; init; }

    public string? Position { get; init; }

    /// <summary>The items it holds, as the live game reports them — sent for the enemies in game (#1750), never in champion select.</summary>
    public IReadOnlyList<int>? Items { get; init; }
}

/// <summary>
/// Turns a <see cref="NextItemRequest"/> into the criteria the next-item read takes — or into
/// nothing when the player's lane is not a position, which the controller answers with a 400.
/// </summary>
internal static class NextItemRequestMapper
{
    private const int MaxAllies = 4;
    private const int MaxEnemies = 5;

    /// <summary>An inventory is seven slots; a purchase history longer than this is not a League game.</summary>
    private const int MaxItems = 30;

    /// <summary>What another participant holds: an inventory is seven slots.</summary>
    private const int MaxHeldItems = 7;

    public static NextItemCriteria? ToCriteria(this NextItemRequest request)
        => LanePositions.Normalize(request.Position) is { } position
            ? new NextItemCriteria
            {
                Position = position,
                Patch = request.Patch,
                Items = (request.Items ?? []).Take(MaxItems).ToList(),
                Allies = Participants(request.Allies, MaxAllies),
                Enemies = Participants(request.Enemies, MaxEnemies),
                GoldLeadAt15 = request.GoldLeadAt15,
            }
            : null;

    private static List<NextItemParticipant> Participants(IReadOnlyList<NextItemRequestParticipant>? raw, int max)
        => [.. (raw ?? [])
            .Where(participant => participant.ChampionId > 0)
            .DistinctBy(participant => participant.ChampionId)
            .Take(max)
            .Select(participant => new NextItemParticipant(
                participant.ChampionId,
                LanePositions.Normalize(participant.Position),
                participant.Items?.Take(MaxHeldItems).ToList()))];
}
