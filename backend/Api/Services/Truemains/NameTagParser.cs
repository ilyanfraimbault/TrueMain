namespace TrueMain.Services.Truemains;

/// <summary>
/// Parses the public <c>{gameName}-{tagLine}</c> URL slug used by truemain
/// routes (e.g. <c>/truemains/Phantasm-EUW1</c>). The separator is the last
/// <c>-</c>, which lets game names contain hyphens (Riot game names allow
/// spaces, hyphens, and most printable characters; tag lines are short
/// alphanumeric strings).
/// </summary>
public static class NameTagParser
{
    public static bool TryParse(string? nameTag, out (string GameName, string TagLine) parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(nameTag))
        {
            return false;
        }

        var idx = nameTag.LastIndexOf('-');
        if (idx <= 0 || idx == nameTag.Length - 1)
        {
            return false;
        }

        var gameName = nameTag[..idx];
        var tagLine = nameTag[(idx + 1)..];
        if (string.IsNullOrWhiteSpace(gameName) || string.IsNullOrWhiteSpace(tagLine))
        {
            return false;
        }

        parsed = (gameName, tagLine);
        return true;
    }
}
