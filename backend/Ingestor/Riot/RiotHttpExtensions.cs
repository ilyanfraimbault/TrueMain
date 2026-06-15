using System.Text.Json;

namespace Ingestor.Riot;

/// <summary>
/// Helpers that read Riot API JSON straight off the response stream instead of
/// buffering the whole payload into memory first.
/// </summary>
internal static class RiotHttpExtensions
{
    // GetFromJsonAsync / ReadFromJsonAsync on a fully-read response buffer the
    // entire body before deserializing. Riot match and timeline payloads run to
    // several MB, so we ask HttpClient to return as soon as the headers arrive
    // (ResponseHeadersRead) and hand the still-flowing content stream to
    // System.Text.Json: peak memory then tracks the deserialized object graph,
    // not a second full copy of the raw bytes. See issue #253.
    public static async Task<T> GetFromJsonStreamingAsync<T>(
        this HttpClient httpClient, Uri uri, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        await response.EnsureSuccessDrainingAsync(ct);
        return await response.ReadFromJsonStreamingAsync<T>(uri, ct);
    }

    // EnsureSuccessStatusCode-equivalent that is safe under ResponseHeadersRead:
    // the body is still unread on error paths, and disposing the response before
    // consuming it makes HttpClient abandon the socket instead of returning it to
    // the connection pool. Draining the (short) error body first lets the
    // connection be reused — relevant when Riot serves a sustained 429/5xx stream
    // — then we surface the failure exactly as EnsureSuccessStatusCode would.
    public static async Task EnsureSuccessDrainingAsync(
        this HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // CopyToAsync(Stream.Null) drains the body without allocating a throwaway
        // buffer the way ReadAsByteArrayAsync would.
        await response.Content.CopyToAsync(Stream.Null, ct);
        response.EnsureSuccessStatusCode();
    }

    // Deserializes an already-fetched response body as a stream. Used by callers
    // that need to inspect the status code (e.g. treat 404 as "not found")
    // before deciding to read the payload.
    public static async Task<T> ReadFromJsonStreamingAsync<T>(
        this HttpResponseMessage response, Uri uri, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        // JsonSerializerOptions.Web matches GetFromJsonAsync's defaults
        // (camelCase, case-insensitive) so behaviour is unchanged.
        var result = await JsonSerializer.DeserializeAsync<T>(stream, JsonSerializerOptions.Web, ct);
        return result ?? throw new InvalidOperationException($"Empty response from Riot API ({uri}).");
    }
}
