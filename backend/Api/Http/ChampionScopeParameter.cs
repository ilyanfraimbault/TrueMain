using System.Diagnostics.CodeAnalysis;
using Core.Lol.Map;
using Microsoft.AspNetCore.Mvc;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Http;

/// <summary>What a blank <c>position</c> means on a route.</summary>
internal enum BlankPosition
{
    /// <summary>A 400: the read cannot run without a lane.</summary>
    Rejected,

    /// <summary>The read resolves the position itself — the dominant lane (scope position null).</summary>
    Resolved,

    /// <summary>Every position summed (scope position <see cref="LanePositions.AllPositions"/>).</summary>
    EveryPosition,
}

/// <summary>
/// How a route reads its <c>position</c> parameter: what a blank value means, and whether the
/// explicit <see cref="LanePositions.AllPositions"/> token is accepted. Named once per route
/// family instead of being implied by which normaliser a controller happened to call.
/// </summary>
internal sealed record PositionRule(BlankPosition WhenBlank, bool AcceptsAllPositions)
{
    /// <summary>A lane is required; <c>ALL</c> is a 400.</summary>
    public static readonly PositionRule Required = new(BlankPosition.Rejected, AcceptsAllPositions: false);

    /// <summary>A lane or <c>ALL</c> is required.</summary>
    public static readonly PositionRule RequiredOrAll = new(BlankPosition.Rejected, AcceptsAllPositions: true);

    /// <summary>Blank resolves to the dominant lane; <c>ALL</c> is a 400.</summary>
    public static readonly PositionRule DominantByDefault = new(BlankPosition.Resolved, AcceptsAllPositions: false);

    /// <summary>Blank resolves to the dominant lane; <c>ALL</c> sums every position.</summary>
    public static readonly PositionRule DominantByDefaultOrAll = new(BlankPosition.Resolved, AcceptsAllPositions: true);

    /// <summary>Blank sums every position; the explicit <c>ALL</c> token is a 400.</summary>
    public static readonly PositionRule EveryByDefault = new(BlankPosition.EveryPosition, AcceptsAllPositions: false);

    /// <summary>Blank and <c>ALL</c> both sum every position.</summary>
    public static readonly PositionRule EveryByDefaultOrAll = new(BlankPosition.EveryPosition, AcceptsAllPositions: true);
}

/// <summary>
/// Parses the champion-scope query parameters (<c>position</c>, <c>patch</c>, <c>eloBracket</c>)
/// into a <see cref="ChampionScope"/>, producing the same 400s the individual parameter
/// parsers own. Position is checked before the elo bracket, as every route did by hand.
/// </summary>
internal static class ChampionScopeParameter
{
    public static bool TryParseChampionScope(
        this ControllerBase controller,
        int championId,
        string? position,
        string? patch,
        string? eloBracket,
        PositionRule rule,
        [NotNullWhen(true)] out ChampionScope? scope,
        [NotNullWhen(false)] out ActionResult? problem,
        ChampionPopulation population = ChampionPopulation.Truemains)
    {
        scope = null;
        if (!TryParsePosition(controller, position, rule, out var normalizedPosition, out problem))
        {
            return false;
        }

        if (!controller.TryNormalizeOptionalEloBracket(eloBracket, out var normalizedBracket, out problem))
        {
            return false;
        }

        scope = new ChampionScope(
            championId,
            normalizedPosition,
            PatchParameter.Normalize(patch),
            normalizedBracket,
            population);
        return true;
    }

    private static bool TryParsePosition(
        ControllerBase controller,
        string? position,
        PositionRule rule,
        out string? normalizedPosition,
        [NotNullWhen(false)] out ActionResult? problem)
    {
        problem = null;
        if (string.IsNullOrWhiteSpace(position))
        {
            normalizedPosition = rule.WhenBlank == BlankPosition.EveryPosition ? LanePositions.AllPositions : null;
            if (rule.WhenBlank != BlankPosition.Rejected)
            {
                return true;
            }

            problem = controller.ValidationProblem(PositionParameter.InvalidMessage);
            return false;
        }

        if (rule.AcceptsAllPositions && PositionParameter.IsAllPositions(position))
        {
            normalizedPosition = LanePositions.AllPositions;
            return true;
        }

        return controller.TryRequirePosition(position, out normalizedPosition, out problem);
    }
}
