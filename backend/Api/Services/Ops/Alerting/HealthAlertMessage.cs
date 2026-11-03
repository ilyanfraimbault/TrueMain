using System.Globalization;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Alerting;

/// <summary>
/// One alert, channel-agnostic (#2292): a title naming what changed, the cockpit's own
/// headline, one line per signal or process that changed, and where to look.
/// </summary>
internal sealed record HealthAlertMessage(
    string Title,
    HealthAlertChangeKind Kind,
    string Verdict,
    string Headline,
    DateTime EvaluatedAtUtc,
    string? HealthUrl,
    IReadOnlyList<HealthAlertChange> Changes)
{
    /// <summary>The admin route the alert points at.</summary>
    public const string HealthPath = "/health";

    /// <summary>
    /// Composes the message for <paramref name="changes"/> (at least one). It is a failure
    /// alert as soon as one key failed — a failure is what needs reading first — and a
    /// recovery otherwise; "recovered" alone means the whole verdict is no longer red.
    /// </summary>
    public static HealthAlertMessage Compose(
        PipelineHealthReadModel health,
        IReadOnlyList<HealthAlertChange> changes,
        AlertingOptions options)
    {
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfZero(changes.Count);

        var failing = changes.Count(change => change.Kind == HealthAlertChangeKind.Failing);
        var kind = failing > 0 ? HealthAlertChangeKind.Failing : HealthAlertChangeKind.Recovered;
        var title = kind == HealthAlertChangeKind.Failing
            ? string.Create(CultureInfo.InvariantCulture, $"Pipeline health: {Count(failing)} failing")
            : health.Status == "red"
                ? string.Create(CultureInfo.InvariantCulture, $"Pipeline health: {Count(changes.Count)} recovered")
                : "Pipeline health recovered";

        if (!string.IsNullOrWhiteSpace(options.EnvironmentLabel))
        {
            title = $"[{options.EnvironmentLabel.Trim()}] {title}";
        }

        return new HealthAlertMessage(
            title,
            kind,
            health.Status,
            health.Headline,
            health.EvaluatedAtUtc,
            HealthUrlOf(options.AdminBaseUrl),
            [.. changes.OrderBy(change => change.Kind).ThenBy(change => change.Key, StringComparer.Ordinal)]);
    }

    /// <summary>The absolute <c>/health</c> URL, or null when no usable admin origin is configured.</summary>
    internal static string? HealthUrlOf(string? adminBaseUrl)
        => Uri.TryCreate(adminBaseUrl?.Trim(), UriKind.Absolute, out var origin)
           && (origin.Scheme == Uri.UriSchemeHttps || origin.Scheme == Uri.UriSchemeHttp)
            ? origin.GetLeftPart(UriPartial.Authority) + HealthPath
            : null;

    private static string Count(int signals) => signals == 1 ? "1 signal" : $"{signals.ToString(CultureInfo.InvariantCulture)} signals";
}
