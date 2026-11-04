using System.Text.Json;
using Data.Entities;

namespace TrueMain.Services.Personas.Pictures;

/// <summary>Reads the picture of one platform's channels.</summary>
public interface IPersonaPictureReader
{
    PersonaLinkKind Kind { get; }

    /// <summary>The picture of the channel at <paramref name="channelUrl"/>. Never throws for a failed read.</summary>
    Task<PersonaPictureLookup> ReadAsync(string channelUrl, CancellationToken ct);
}

/// <summary>
/// What every platform's reader shares: one HTTP client, and one reading of a failure. A missing
/// channel is no picture; anything else that goes wrong says nothing about the channel.
/// </summary>
public abstract class PersonaPictureReader(IHttpClientFactory httpClients, ILogger logger) : IPersonaPictureReader
{
    public const string HttpClientName = "PersonaPictures";

    public abstract PersonaLinkKind Kind { get; }

    protected abstract Task<PersonaPictureLookup> ReadChannelAsync(HttpClient http, string channelUrl, CancellationToken ct);

    public async Task<PersonaPictureLookup> ReadAsync(string channelUrl, CancellationToken ct)
    {
        try
        {
            using var http = httpClients.CreateClient(HttpClientName);
            return await ReadChannelAsync(http, channelUrl, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // The message, not the exception: no platform's answer belongs in the logs as a stack trace.
            logger.LogWarning("Reading the {Kind} picture of a known player failed: {Error}", Kind, ex.GetType().Name);
            return PersonaPictureLookup.Unavailable;
        }
    }

    /// <summary>
    /// The JSON body of a request, or the lookup that answers it without one: no picture for a channel
    /// the platform does not know, unavailable for everything else. A 400 included: it is how YouTube
    /// answers a revoked key, and reading that as no picture would wipe every stored one.
    /// </summary>
    protected static async Task<(JsonDocument? Body, PersonaPictureLookup? Failure)> ReadJsonAsync(
        HttpClient http, HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return (await JsonDocument.ParseAsync(stream, cancellationToken: ct), null);
        }

        return (null, response.StatusCode == System.Net.HttpStatusCode.NotFound
            ? PersonaPictureLookup.None
            : PersonaPictureLookup.Unavailable);
    }

    /// <summary>The string at <paramref name="path"/> below <paramref name="element"/>, null when any step is missing or null.</summary>
    protected static string? StringAt(JsonElement element, params string[] path)
    {
        foreach (var name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
            {
                return null;
            }
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }
}
