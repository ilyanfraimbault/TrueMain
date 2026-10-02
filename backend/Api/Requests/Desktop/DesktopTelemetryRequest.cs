using System.ComponentModel.DataAnnotations;

namespace TrueMain.Requests.Desktop;

/// <summary>
/// Request body for <c>POST /desktop/telemetry</c> (#1805): what one install of the desktop app
/// counted since its previous batch. Counters, not events — the app adds them up between two
/// sends, and the API adds the batch to the install's day.
/// </summary>
/// <remarks>
/// Anyone can post this: the endpoint is public, because the app has no secret it could keep.
/// The bounds below are what a real install can produce in one batch, so a forged one can
/// inflate a counter only as fast as the rate limiter lets one address post.
/// </remarks>
public sealed record DesktopTelemetryRequest
{
    /// <summary>The install's anonymous id, drawn by the app on its first launch.</summary>
    [Required]
    public Guid? InstallId { get; init; }

    /// <summary>The app's version, e.g. <c>0.3.1</c> or <c>0.3.2-beta.4</c>; checked by the service.</summary>
    [Required]
    [MaxLength(32)]
    public string? AppVersion { get; init; }

    [Required]
    [AllowedValues("macos", "windows", "linux")]
    public string? Os { get; init; }

    /// <summary>True in the batches of the launch that drew <see cref="InstallId"/>.</summary>
    public bool FirstLaunch { get; init; }

    [Range(0, 20)]
    public int Launches { get; init; }

    /// <summary>A batch covers at most a day of the app being open.</summary>
    [Range(0, 1_440)]
    public int OpenMinutes { get; init; }

    /// <summary>Page key → views. Keys outside the catalog are dropped, not rejected.</summary>
    public IReadOnlyDictionary<string, int>? PageViews { get; init; }

    /// <summary>Feature key → uses. Keys outside the catalog are dropped, not rejected.</summary>
    public IReadOnlyDictionary<string, int>? Features { get; init; }
}
