using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TrueMain.Options;

namespace TrueMain.Services.Ops.Alerting;

/// <summary>Where an alert goes (#2292). Throws when the channel did not take it.</summary>
internal interface IHealthAlertNotifier
{
    /// <summary>The channel's name as recorded with each alert, e.g. <c>discord</c>.</summary>
    string Channel { get; }

    Task SendAsync(HealthAlertMessage message, CancellationToken ct);
}

/// <summary>
/// Posts alerts to a Discord webhook (#2292) — the channel decided for the operator's phone.
/// One embed per alert: red for a failure, green for a recovery, linked to <c>/health</c>.
/// </summary>
/// <remarks>
/// The webhook URL is a credential, so its HTTP client is registered without the request
/// loggers (which would print it) and nothing here puts it in a log or an exception message.
/// </remarks>
internal sealed class DiscordHealthAlertNotifier(IHttpClientFactory httpClientFactory, IOptions<AlertingOptions> options)
    : IHealthAlertNotifier
{
    public const string HttpClientName = "health-alerts-discord";

    public string Channel => "discord";

    public async Task SendAsync(HealthAlertMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        var webhook = options.Value.DiscordWebhookUrl
                      ?? throw new InvalidOperationException("No Discord webhook is configured.");
        using var client = httpClientFactory.CreateClient(HttpClientName);
        // A buffered body, so the request carries a Content-Length rather than a chunked stream.
        using var content = new StringContent(
            JsonSerializer.Serialize(DiscordWebhookPayload.From(message)),
            Encoding.UTF8,
            "application/json");
        using var response = await client.PostAsync(webhook, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            // The status alone: the request URI is the secret.
            throw new HttpRequestException(
                string.Create(CultureInfo.InvariantCulture, $"Discord answered {(int)response.StatusCode}."),
                inner: null,
                response.StatusCode);
        }
    }
}

/// <summary>
/// The body Discord's "execute webhook" endpoint takes, reduced to what an alert uses. Mentions
/// are disabled outright so a headline can never ping a channel.
/// </summary>
internal sealed record DiscordWebhookPayload(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("embeds")] IReadOnlyList<DiscordEmbed> Embeds,
    [property: JsonPropertyName("allowed_mentions")] DiscordAllowedMentions AllowedMentions)
{
    /// <summary>Discord's own limits on an embed.</summary>
    internal const int MaxFields = 25;
    internal const int MaxFieldValue = 1024;
    internal const int MaxTitle = 256;

    internal const int FailingColor = 0xE5484D;
    internal const int RecoveredColor = 0x30A46C;

    public static DiscordWebhookPayload From(HealthAlertMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var fields = message.Changes
            .Take(MaxFields)
            .Select(change => new DiscordEmbedField(
                Truncate(
                    change.Kind == HealthAlertChangeKind.Failing
                        ? $"{change.Title} — failing ({change.Status})"
                        : $"{change.Title} — recovered ({change.Status})",
                    MaxTitle),
                Truncate(string.IsNullOrWhiteSpace(change.Headline) ? "—" : change.Headline, MaxFieldValue)))
            .ToList();

        var link = message.HealthUrl is null
            ? $"Open {HealthAlertMessage.HealthPath} in the admin portal."
            : $"Open {message.HealthUrl}";
        var description = string.Create(
            CultureInfo.InvariantCulture,
            $"{message.Headline}\nVerdict: **{message.Verdict}**\n{link}");

        var embed = new DiscordEmbed(
            Truncate(message.Title, MaxTitle),
            description,
            message.HealthUrl,
            message.Kind == HealthAlertChangeKind.Failing ? FailingColor : RecoveredColor,
            DateTime.SpecifyKind(message.EvaluatedAtUtc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
            fields);

        return new DiscordWebhookPayload("TrueMain health", [embed], new DiscordAllowedMentions([]));
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : string.Concat(value.AsSpan(0, max - 1), "…");
}

internal sealed record DiscordEmbed(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url,
    [property: JsonPropertyName("color")] int Color,
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("fields")] IReadOnlyList<DiscordEmbedField> Fields);

internal sealed record DiscordEmbedField(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("inline")] bool Inline = false);

internal sealed record DiscordAllowedMentions(
    [property: JsonPropertyName("parse")] IReadOnlyList<string> Parse);
