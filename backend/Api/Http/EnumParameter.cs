using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;

namespace TrueMain.Http;

/// <summary>
/// An optional query parameter that names an enum member (case-insensitive): a blank value
/// means "no filter", anything else must be a defined member or the request is a 400 that
/// lists the accepted names.
/// </summary>
internal static class EnumParameter
{
    public static bool TryParseOptionalEnum<TEnum>(
        this ControllerBase controller,
        string? raw,
        string parameterName,
        out TEnum? value,
        [NotNullWhen(false)] out ActionResult? problem)
        where TEnum : struct, Enum
    {
        value = null;
        problem = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (Enum.TryParse<TEnum>(raw.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            value = parsed;
            return true;
        }

        problem = controller.ValidationProblem(
            $"{parameterName} '{raw}' is not one of {string.Join(", ", Enum.GetNames<TEnum>())}.");
        return false;
    }
}
