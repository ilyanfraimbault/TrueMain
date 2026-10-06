using System.Text.RegularExpressions;

namespace TrueMain.Services.Desktop;

/// <summary>
/// What the desktop app may report (#1805): the pages and features it counts, named here once
/// so the write path stores nothing else and the admin page has a fixed set of rows to show.
/// The app's side of the list is <c>desktop/app/app/utils/telemetry.ts</c> — a key added there
/// and not here is dropped by the API, which is what lets an older API run under a newer app.
/// </summary>
public static partial class DesktopTelemetryCatalog
{
    /// <summary>One key per page of the app's own routes; the overlay panels and the dev tools are not pages.</summary>
    public static readonly IReadOnlyList<string> Pages =
    [
        "dashboard",
        "champions",
        "champion",
        "tierlist",
        "matchup",
        "truemains",
        "favorites",
        "draft",
        "game",
        "overlay",
        "recordings",
        "recording",
        "clip",
    ];

    /// <summary>What the app did for the player, beyond showing a page.</summary>
    public static readonly IReadOnlyList<string> Features =
    [
        // A champion select the app followed.
        "champSelect",
        // A game the app followed live.
        "liveGame",
        // A game in which the overlay was drawn over League.
        "overlayShown",
        // A game the app recorded.
        "gameRecorded",
        // A clip cut from a recording and saved, from the recap.
        "clipSaved",
        // A full game the player chose to keep, exempt from the storage budget.
        "gameKept",
        // A full game the player deleted by hand.
        "recordingDeleted",
        // A page of the site the app opened in the browser.
        "siteOpened",
        // A pick hovered from the app's draft screen, on the player's click.
        "champSelectHover",
        // A pick locked in from the app's draft screen.
        "champSelectLock",
        // A ban made from the app's draft screen.
        "champSelectBan",
    ];

    private static readonly HashSet<string> PageSet = new(Pages, StringComparer.Ordinal);
    private static readonly HashSet<string> FeatureSet = new(Features, StringComparer.Ordinal);

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
