using Data.Configurations;

namespace TrueMain.Services.Personas.Pictures;

/// <summary>What asking one channel for its picture gave.</summary>
public enum PersonaPictureOutcome
{
    /// <summary>The channel has a picture of its own.</summary>
    Found,

    /// <summary>The channel is gone, or still shows the platform's default picture.</summary>
    None,

    /// <summary>No answer worth believing: a timeout, a quota, an outage. Says nothing about the channel.</summary>
    Unavailable,
}

public sealed record PersonaPictureLookup(PersonaPictureOutcome Outcome, string? Url = null)
{
    public static readonly PersonaPictureLookup None = new(PersonaPictureOutcome.None);

    public static readonly PersonaPictureLookup Unavailable = new(PersonaPictureOutcome.Unavailable);

    /// <summary>
    /// The hosts a picture may come from, the ones the site's image route and the desktop app's CSP
    /// allow (<c>web/shared/utils/ipx.ts</c>, <c>desktop/src-tauri/tauri.conf.json</c>). Anything else
    /// would draw a broken image, so it counts as no picture and the next channel gets its turn.
    /// </summary>
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "yt3.ggpht.com",
        "yt3.googleusercontent.com",
        "static-cdn.jtvnw.net",
        "files.kick.com",
        "profile.img.sooplive.co.kr",
        "nng-phinf.pstatic.net",
    };

    /// <summary>The lookup for the picture URL a platform answered, null or empty when it has none.</summary>
    public static PersonaPictureLookup FromUrl(string? url) =>
        url is { Length: > 0 and <= PersonaConfiguration.PictureUrlMaxLength }
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && AllowedHosts.Contains(uri.Host)
            ? new PersonaPictureLookup(PersonaPictureOutcome.Found, url)
            : None;
}
