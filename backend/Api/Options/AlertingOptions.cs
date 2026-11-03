using System.ComponentModel.DataAnnotations;

namespace TrueMain.Options;

/// <summary>
/// The pipeline-health alerter (#2292), bound from <c>Alerting:*</c>: where alerts go, and
/// the debounce, cooldown and failure-streak levels that decide when one is sent.
///
/// <para>
/// Off unless <see cref="DiscordWebhookUrl"/> is set. That URL is a credential — whoever
/// holds it can post to the channel — so it comes from the stack's environment only, is never
/// committed, never logged, and never shown on the configuration page: that page's
/// allow-list reads <see cref="Enabled"/> in its place.
/// </para>
/// </summary>
public sealed class AlertingOptions
{
    public const string SectionName = "Alerting";

    /// <summary>The Discord webhook alerts are posted to. Empty turns alerting off.</summary>
    public string? DiscordWebhookUrl { get; set; }

    /// <summary>Whether alerts are sent: a webhook is configured and is an absolute http(s) URL.</summary>
    public bool Enabled
        => Uri.TryCreate(DiscordWebhookUrl, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// <summary>
    /// The admin portal's origin, e.g. <c>https://admin.example.com</c>: alerts link to its
    /// <c>/health</c> page. Empty leaves the link out and names the path instead.
    /// </summary>
    public string? AdminBaseUrl { get; set; }

    /// <summary>A short label prefixed to every alert title (<c>prod</c>, <c>preprod</c>), so two stacks sharing a channel stay apart.</summary>
    public string? EnvironmentLabel { get; set; }

    /// <summary>How often the verdict is evaluated.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "1.00:00:00")]
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Consecutive evaluations a signal must read red before it counts as failing, so one bad
    /// sample — a cold read timing out, a deploy — is not an incident.
    /// </summary>
    [Range(1, 20)]
    public int RedConfirmations { get; set; } = 2;

    /// <summary>
    /// Minimum time between two failure alerts for the same signal or process: a signal that
    /// flaps red, green, red inside it is announced once.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "7.00:00:00")]
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Consecutive failed runs at which a process is announced on its own.</summary>
    [Range(1, 1000)]
    public int ProcessFailureThreshold { get; set; } = 3;
}
