using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;

namespace TrueMain.Http;

/// <summary>
/// The <c>granularity</c> query parameter: a required, closed enum whose 400 message is
/// derived from the enum itself, so the message cannot drift from the set it describes.
/// </summary>
internal static class GranularityParameter
{
    /// <summary>
    /// Parses a required, closed <c>granularity</c> query parameter case-insensitively;
    /// anything else is a 400 whose detail lists the allowed values.
    /// </summary>
    public static bool TryParseGranularity<TGranularity>(
        this ControllerBase controller,
        string? granularity,
        out TGranularity parsed,
        [NotNullWhen(false)] out ActionResult? problem)
        where TGranularity : struct, Enum
    {
        if (Enum.TryParse(granularity, ignoreCase: true, out parsed) && Enum.IsDefined(parsed))
        {
            problem = null;
            return true;
        }

        var allowed = string.Join(", ", Enum.GetNames<TGranularity>().Select(name => name.ToLowerInvariant()));
        controller.ModelState.AddModelError(
            nameof(granularity),
            $"granularity is required and must be one of: {allowed}.");
        problem = controller.ValidationProblem(controller.ModelState);
        return false;
    }
}
