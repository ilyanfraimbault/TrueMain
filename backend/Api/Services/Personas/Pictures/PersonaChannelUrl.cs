using System.Text.RegularExpressions;

namespace TrueMain.Services.Personas.Pictures;

/// <summary>Reads the channel a stored link names, the way each platform's API asks for it.</summary>
public static partial class PersonaChannelUrl
{
    /// <summary>
    /// The query parameter and value that name a YouTube channel to the Data API; null for the
    /// legacy <c>/c/name</c> and <c>/name</c> forms. The API cannot look those up, and the same word
    /// read as a handle could be somebody else's channel: a stranger's face is worse than no picture.
    /// </summary>
    public static (string Parameter, string Value)? YouTube(string url)
    {
        var segments = Segments(url);
        return segments switch
        {
            [var handle] when handle.StartsWith('@') && handle.Length > 1 => ("forHandle", handle),
            ["channel", var id] when id.StartsWith("UC", StringComparison.Ordinal) => ("id", id),
            ["user", var name] => ("forUsername", name),
            _ => null,
        };
    }

    /// <summary>The login of a Twitch channel, null when the link does not name one.</summary>
    public static string? Twitch(string url) => FirstSegmentMatching(url, TwitchLogin());

    /// <summary>The slug of a Kick channel, null when the link does not name one.</summary>
    public static string? Kick(string url) => FirstSegmentMatching(url, KickSlug());

    /// <summary>The station id of a SOOP channel (<c>/station/{id}</c>), null when the link does not name one.</summary>
    public static string? Soop(string url) =>
        Segments(url) is ["station", var id] && SoopId().IsMatch(id) ? id : null;

    /// <summary>The channel id of a Chzzk channel, null when the link does not name one.</summary>
    public static string? Chzzk(string url) => FirstSegmentMatching(url, ChzzkId());

    private static string? FirstSegmentMatching(string url, Regex pattern) =>
        Segments(url) is [var segment] && pattern.IsMatch(segment) ? segment : null;

    private static string[] Segments(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray()
            : [];

    [GeneratedRegex("^[A-Za-z0-9_]{1,25}$")]
    private static partial Regex TwitchLogin();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex KickSlug();

    [GeneratedRegex("^[A-Za-z0-9_]{1,64}$")]
    private static partial Regex SoopId();

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex ChzzkId();
}
