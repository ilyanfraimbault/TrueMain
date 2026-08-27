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

    /// <summary>
    /// Upper bound on a Riot ID accepted by <see cref="TryParseRiotId"/>. The
    /// database caps GameName at 32 and TagLine at 8, so anything longer is junk
    /// or abuse and is rejected before it reaches a query. Mirrors
    /// <c>SearchQueryService.MaxQueryLength</c>. Deliberately not applied by
    /// <see cref="TryParse"/> itself, which stays a bare separator split; since
    /// #1230 every route resolves through <see cref="TryParseRiotId"/>, so the
    /// cap now covers route segments too — with no behaviour to lose, since a
    /// stored Riot ID cannot exceed 41 characters and so could never have
    /// matched a longer slug anyway.
    /// </summary>
    public const int MaxRiotIdLength = 64;

    /// <summary>
    /// Parses a Riot ID the way a player types it — <c>Name#TAG</c> — and falls
    /// back to the URL slug form handled by <see cref="TryParse"/> when there is
    /// no <c>#</c>. Accepting both forms is what lets a single parser serve the
    /// endpoints that take a Riot ID as a query parameter (the mains comparison,
    /// #528), where the typed form is what a user pastes in, and the truemain
    /// routes that take it as a path segment — since #1230 both resolve through
    /// <c>TruemainAccountResolver</c>, which calls this.
    ///
    /// This is the single definition of "well-formed Riot ID" shared by the
    /// controller (which turns a failure into a 400) and the query service
    /// (which parses defensively because it is reachable outside MVC), so the
    /// two can never disagree on what they accept.
    /// </summary>
    public static bool TryParseRiotId(string? riotId, out (string GameName, string TagLine) parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(riotId) || riotId.Length > MaxRiotIdLength)
        {
            return false;
        }

        var trimmed = riotId.Trim();
        var hash = trimmed.IndexOf('#');
        if (hash < 0)
        {
            return TryParse(trimmed, out parsed);
        }

        // The first '#' separates the two halves: Riot game names cannot
        // contain one, so a second '#' is part of a junk tag and fails below.
        var gameName = trimmed[..hash].Trim();
        var tagLine = trimmed[(hash + 1)..].Trim();
        if (gameName.Length == 0 || tagLine.Length == 0 || tagLine.Contains('#'))
        {
            return false;
        }

        parsed = (gameName, tagLine);
        return true;
    }
}
