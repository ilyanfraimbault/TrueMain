using System.ComponentModel.DataAnnotations;

namespace TrueMain.Requests.Internal;

/// <summary>
/// Request body for <c>POST /internal/desktop/downloads</c> (#1805): one installer the site's
/// download route just redirected a visitor to.
/// </summary>
public sealed record DesktopDownloadRequest
{
    /// <summary>The site's own platform names (<c>/api/desktop/download/{platform}</c>).</summary>
    [Required]
    [AllowedValues("mac", "windows")]
    public string? Platform { get; init; }

    /// <summary>The release's version, without the tag's <c>desktop-v</c> prefix; checked by the service.</summary>
    [Required]
    [MaxLength(32)]
    public string? Version { get; init; }
}
