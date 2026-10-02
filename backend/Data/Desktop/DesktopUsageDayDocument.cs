using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.Desktop;

/// <summary>
/// What one install of the desktop app did on one UTC day (#1805), in the
/// <c>desktop_usage_days</c> collection.
///
/// <para>
/// <b>An aggregate, not an event log.</b> The app counts on its side and sends the counters
/// every few minutes; each batch is an <c>$inc</c> into this day's document. Every question
/// the admin page asks — active installs, launches, time open, pages and features — is a sum
/// or a distinct count over these documents, so storing each click would only multiply the
/// rows the page then folds back together.
/// </para>
///
/// <para>
/// <b>Anonymous by construction.</b> <see cref="InstallId"/> is a random id the app draws on
/// its first launch and keeps on that machine. Nothing the app reads from the League client
/// — Riot ID, PUUID, rank — ever reaches this collection, and the client's address is not
/// stored.
/// </para>
/// </summary>
public sealed class DesktopUsageDayDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>The UTC day, at midnight. Half of the upsert key, and the TTL field.</summary>
    [BsonElement("dayUtc")]
    public DateTime DayUtc { get; set; }

    /// <summary>The install's anonymous id, as the app sent it (a GUID). The other half of the key.</summary>
    [BsonElement("installId")]
    public string InstallId { get; set; } = string.Empty;

    /// <summary>The app version of the day's latest batch: an update mid-day shows as the newer one.</summary>
    [BsonElement("appVersion")]
    public string AppVersion { get; set; } = string.Empty;

    /// <summary><c>macos</c>, <c>windows</c> or <c>linux</c>.</summary>
    [BsonElement("os")]
    public string Os { get; set; } = string.Empty;

    /// <summary>True on the day the install drew its id: the app's first launch on that machine.</summary>
    [BsonElement("firstLaunch")]
    public bool FirstLaunch { get; set; }

    [BsonElement("launches")]
    public int Launches { get; set; }

    /// <summary>Minutes the app's window was open, counted by the app a minute at a time.</summary>
    [BsonElement("openMinutes")]
    public int OpenMinutes { get; set; }

    /// <summary>Page key → views. Keys come from the API's allow-list only.</summary>
    [BsonElement("pageViews")]
    public Dictionary<string, int> PageViews { get; set; } = [];

    /// <summary>Feature key → uses. Keys come from the API's allow-list only.</summary>
    [BsonElement("features")]
    public Dictionary<string, int> Features { get; set; } = [];

    [BsonElement("firstSeenUtc")]
    public DateTime FirstSeenUtc { get; set; }

    [BsonElement("lastSeenUtc")]
    public DateTime LastSeenUtc { get; set; }
}

/// <summary>
/// Installers downloaded from the site on one UTC day, for one platform and version (#1805),
/// in the <c>desktop_download_days</c> collection.
/// </summary>
public sealed class DesktopDownloadDayDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("dayUtc")]
    public DateTime DayUtc { get; set; }

    /// <summary><c>mac</c> or <c>windows</c> — the site's download route's own names.</summary>
    [BsonElement("platform")]
    public string Platform { get; set; } = string.Empty;

    /// <summary>The version the redirect served, e.g. <c>0.3.1</c> or <c>0.3.2-beta.4</c>.</summary>
    [BsonElement("version")]
    public string Version { get; set; } = string.Empty;

    [BsonElement("count")]
    public long Count { get; set; }
}
