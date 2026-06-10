namespace TrueMain.Services.Ops;

/// <summary>
/// Helpers for safely using user-supplied text inside <c>LIKE</c>/<c>ILIKE</c>
/// patterns. Escapes the wildcard metacharacters (<c>%</c>, <c>_</c>) and the
/// escape character itself so the input matches literally; pair the produced
/// pattern with the <c>"\\"</c> escape-character argument on
/// <c>EF.Functions.Like/ILike</c>.
/// </summary>
internal static class LikeEscaping
{
    public const string EscapeChar = "\\";

    public static string Escape(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
