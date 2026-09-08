using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;
using TrueMain.Services.Truemains.Identity;

namespace TrueMain.Http;

/// <summary>
/// A Riot ID passed as a query parameter (<c>Name#TAG</c>). Only well-formedness is checked
/// here — whether an account exists is an answer the query services give, not a client error.
/// </summary>
/// <remarks>
/// Keeping the two apart is the point: a caller's "we don't track this account yet" state must
/// never fire on a string that is not a Riot ID at all. Validated with the same parser the
/// services resolve with, so the two cannot disagree on what they accept.
/// </remarks>
internal static class RiotIdParameter
{
    /// <summary>
    /// 400 detail for a Riot ID query parameter that isn't well-formed. Names the offending
    /// parameter so a caller passing several can tell which failed.
    /// </summary>
    public static string InvalidMessage(string parameterName)
        => $"{parameterName} must be a Riot ID of the form Name#TAG "
           + $"(at most {NameTagParser.MaxRiotIdLength} characters).";

    /// <summary>
    /// Validates a required Riot ID: missing, blank, or not well-formed is a 400. The value is
    /// not rewritten — the services take the raw Riot ID and parse it themselves.
    /// </summary>
    public static bool TryRequireRiotId(
        this ControllerBase controller,
        string? riotId,
        string parameterName,
        [NotNullWhen(false)] out ActionResult? problem)
    {
        if (string.IsNullOrWhiteSpace(riotId))
        {
            problem = controller.ValidationProblem(
                $"{parameterName} is required — pass the Riot ID as Name#TAG.");
            return false;
        }

        return controller.TryValidateOptionalRiotId(riotId, parameterName, out problem);
    }

    /// <summary>
    /// Validates an optional Riot ID: blank passes (the parameter is simply absent), a
    /// non-blank value that isn't well-formed is a 400.
    /// </summary>
    public static bool TryValidateOptionalRiotId(
        this ControllerBase controller,
        string? riotId,
        string parameterName,
        [NotNullWhen(false)] out ActionResult? problem)
    {
        if (!string.IsNullOrWhiteSpace(riotId) && !NameTagParser.TryParseRiotId(riotId, out _))
        {
            problem = controller.ValidationProblem(InvalidMessage(parameterName));
            return false;
        }

        problem = null;
        return true;
    }
}
