using System.Text.RegularExpressions;

namespace TrueMain.Services.Desktop;

/// <summary>
/// What the desktop app may report (#1805): the pages and features it counts, each with the name
/// the admin page shows, listed here once so the write path stores nothing else and the admin page
/// has a fixed set of rows. The app emits the keys — the route map in
/// <c>desktop/app/app/utils/telemetry.ts</c>, the shell's <c>Feature</c> in
/// <c>desktop/src-tauri/src/telemetry.rs</c> — and their tests fail on a key missing here. A key
/// the app sends and this list lacks is dropped, which is what lets an older API run under a newer app.
/// </summary>
public static partial class DesktopTelemetryCatalog
{
    /// <summary>One key per page of the app's own routes; the overlay panels and the dev tools are not pages.</summary>
    public static readonly IReadOnlyList<DesktopTelemetryKey> Pages =
    [
        new("dashboard", "Dashboard"),
        new("champions", "Champions"),
        new("champion", "Champion page"),
        new("tierlist", "Tier list"),
        new("matchup", "Matchup"),
        new("truemains", "Truemains"),
        new("favorites", "Favorites"),
        new("draft", "Champ select"),
        new("game", "Game"),
        new("overlay", "Overlay"),
        new("recordings", "Recordings"),
        new("recording", "Recording"),
        new("clip", "Clip"),
    ];

    /// <summary>
    /// What the app did for the player, beyond showing a page. Each label says what was counted:
    /// an occurrence in which the app did that for the player.
    /// </summary>
    public static readonly IReadOnlyList<DesktopTelemetryKey> Features =
    [
        new("champSelect", "Champion select followed"),
        new("liveGame", "Game followed live"),
        new("overlayShown", "Games with the overlay drawn"),
        new("gameRecorded", "Games recorded"),
        new("clipSaved", "Clips saved"),
        new("gameKept", "Full games kept"),
        new("recordingDeleted", "Recordings deleted by hand"),
        new("siteOpened", "Site pages opened"),
        new("champSelectHover", "Picks hovered from the app"),
        new("champSelectLock", "Picks locked in from the app"),
        new("champSelectBan", "Bans made from the app"),
        new("youtubeUploadOpened", "YouTube upload opened from a clip"),
        new("clipPublished", "Clips published on YouTube"),
        new("clipVideoDeleted", "Published clips removed from disk"),
    ];

    private static readonly HashSet<string> PageSet = new(Pages.Select(page => page.Key), StringComparer.Ordinal);
    private static readonly HashSet<string> FeatureSet = new(Features.Select(feature => feature.Key), StringComparer.Ordinal);

    public static bool IsPage(string key) => PageSet.Contains(key);

    public static bool IsFeature(string key) => FeatureSet.Contains(key);

    /// <summary>
    /// A release version as the desktop tags carry it — <c>0.3.1</c>, <c>0.3.2-beta.4</c> — and
    /// nothing else, since the admin page lists versions as they were sent.
    /// </summary>
    public static bool IsVersion(string? value) => value is { Length: <= 32 } && VersionPattern().IsMatch(value);

    [GeneratedRegex(@"^\d{1,4}\.\d{1,4}\.\d{1,4}(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}

/// <summary>A page or feature the app counts, and the name the admin page shows for it.</summary>
public sealed record DesktopTelemetryKey(string Key, string Label);
