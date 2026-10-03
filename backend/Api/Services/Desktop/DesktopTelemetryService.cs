using Data.Desktop;
using TrueMain.Requests.Desktop;
using TrueMain.Requests.Internal;

namespace TrueMain.Services.Desktop;

public interface IDesktopTelemetryService
{
    /// <summary>Folds an app batch into its install's day; the errors that rejected it otherwise.</summary>
    Task<IReadOnlyDictionary<string, string>> RecordUsageAsync(DesktopTelemetryRequest request, CancellationToken ct);

    /// <summary>Counts one installer download; the errors that rejected it otherwise.</summary>
    Task<IReadOnlyDictionary<string, string>> RecordDownloadAsync(DesktopDownloadRequest request, CancellationToken ct);
}

/// <summary>
/// The write side of the desktop telemetry (#1805). Model validation checks the shape; this
/// checks what needs the catalog, then hands the store a batch it can write as is.
/// </summary>
public sealed class DesktopTelemetryService(IDesktopTelemetryStore store, TimeProvider timeProvider)
    : IDesktopTelemetryService
{
    /// <summary>Views of one page, or uses of one feature, in a single batch of a few minutes.</summary>
    internal const int MaxCountPerKey = 500;

    private static readonly IReadOnlyDictionary<string, string> NoErrors = new Dictionary<string, string>();

    public async Task<IReadOnlyDictionary<string, string>> RecordUsageAsync(
        DesktopTelemetryRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.InstallId is not { } installId || installId == Guid.Empty)
        {
            errors["installId"] = "The install id must be a non-empty GUID.";
        }

        if (!DesktopTelemetryCatalog.IsVersion(request.AppVersion))
        {
            errors["appVersion"] = "The app version must read like 0.3.1 or 0.3.2-beta.4.";
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        var batch = new DesktopUsageBatch(
            request.InstallId!.Value,
            request.AppVersion!,
            request.Os!,
            request.FirstLaunch,
            request.Launches,
            request.OpenMinutes,
            Counters(request.PageViews, DesktopTelemetryCatalog.IsPage),
            Counters(request.Features, DesktopTelemetryCatalog.IsFeature));

        await store.RecordUsageAsync(batch, timeProvider.GetUtcNow().UtcDateTime, ct);
        return NoErrors;
    }

    public async Task<IReadOnlyDictionary<string, string>> RecordDownloadAsync(
        DesktopDownloadRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!DesktopTelemetryCatalog.IsVersion(request.Version))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["version"] = "The version must read like 0.3.1 or 0.3.2-beta.4."
            };
        }

        await store.RecordDownloadAsync(
            request.Platform!, request.Version!, timeProvider.GetUtcNow().UtcDateTime, ct);
        return NoErrors;
    }

    /// <summary>
    /// The counters the catalog knows, each capped. An unknown key is dropped rather than
    /// failing the batch: it is a newer app naming a page this API does not list yet, and the
    /// rest of its batch is still good.
    /// </summary>
    internal static IReadOnlyDictionary<string, int> Counters(
        IReadOnlyDictionary<string, int>? counters,
        Func<string, bool> known)
    {
        var kept = new Dictionary<string, int>(StringComparer.Ordinal);
        if (counters is null)
        {
            return kept;
        }

        foreach (var (key, count) in counters)
        {
            if (count > 0 && known(key))
            {
                kept[key] = Math.Min(count, MaxCountPerKey);
            }
        }

        return kept;
    }
}
