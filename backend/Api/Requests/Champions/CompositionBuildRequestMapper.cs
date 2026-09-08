using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;
using TrueMain.Http;
using TrueMain.Services.Champions.Composition;

namespace TrueMain.Requests.Champions;

/// <summary>
/// Turns a <see cref="CompositionBuildRequest"/> body into the search criteria the composition
/// services take, or into the 400 that rejects it. Colocated with the request type it
/// validates: the shape of a draft and the rules for what counts as a valid one are one
/// decision, and splitting them lets the two drift.
/// </summary>
internal static class CompositionBuildRequestMapper
{
    /// <summary>
    /// Validates and normalises a composition draft body into search criteria.
    /// Shared by the recommendation and its provenance listing so the two can
    /// never disagree on what a draft means — or on which drafts are rejected.
    /// </summary>
    public static bool TryBuildCompositionCriteria(
        this ControllerBase controller,
        int championId,
        CompositionBuildRequest request,
        out CompositionSearchCriteria criteria,
        [NotNullWhen(false)] out ActionResult? problem)
    {
        criteria = null!;

        if (championId <= 0)
        {
            problem = controller.ValidationProblem("championId must be a positive champion id.");
            return false;
        }

        if (!controller.TryRequirePosition(request.Position, out var normalizedPosition, out var positionProblem))
        {
            problem = positionProblem;
            return false;
        }

        if (!controller.TryNormalizeSlots(request.Allies, "allies", out var allies, out var slotProblem)
            || !controller.TryNormalizeSlots(request.Enemies, "enemies", out var enemies, out slotProblem))
        {
            problem = slotProblem;
            return false;
        }

        if (!controller.TryNormalizeOptionalEloBracket(request.EloBracket, out var normalizedBracket, out var bracketProblem))
        {
            problem = bracketProblem;
            return false;
        }

        if (allies.ContainsKey(normalizedPosition))
        {
            problem = controller.ValidationProblem(
                "allies must not contain the player's own position — that slot is the champion of the route.");
            return false;
        }

        criteria = new CompositionSearchCriteria
        {
            ChampionId = championId,
            Position = normalizedPosition,
            Allies = allies,
            Enemies = enemies,
            Patch = PatchParameter.Normalize(request.Patch),
            EloBracket = normalizedBracket,
        };
        problem = null;
        return true;
    }

    /// <summary>
    /// Canonicalises one team's slot list into a position→champion map; any
    /// non-positive champion id, unrecognised position, or duplicated position
    /// within the team yields a 400 <paramref name="problem"/>. Null tolerated:
    /// the DTO defaults to an empty list, but an explicit <c>"allies": null</c>
    /// in the JSON body overrides that default with null at binding time.
    /// </summary>
    public static bool TryNormalizeSlots(
        this ControllerBase controller,
        IReadOnlyList<CompositionSlotInput>? slots,
        string teamLabel,
        out Dictionary<string, int> byPosition,
        [NotNullWhen(false)] out ActionResult? problem)
    {
        byPosition = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var slot in slots ?? [])
        {
            // A literal null entry in the JSON array binds as a null element.
            if (slot is null || slot.ChampionId <= 0)
            {
                problem = controller.ValidationProblem($"{teamLabel} contains a slot without a positive championId.");
                return false;
            }

            var position = PositionParameter.Normalize(slot.Position);
            if (position is null)
            {
                problem = controller.ValidationProblem(
                    $"{teamLabel}: {PositionParameter.InvalidMessage}");
                return false;
            }

            if (!byPosition.TryAdd(position, slot.ChampionId))
            {
                problem = controller.ValidationProblem($"{teamLabel} contains two slots at position {position}.");
                return false;
            }
        }

        problem = null;
        return true;
    }
}
