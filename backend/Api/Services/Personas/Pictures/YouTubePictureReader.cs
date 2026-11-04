using Data.Entities;
using Microsoft.Extensions.Options;
using TrueMain.Options;

namespace TrueMain.Services.Personas.Pictures;

/// <summary>
/// A YouTube channel's picture, through the Data API's <c>channels.list</c> (one quota unit a call).
/// The key travels in a header, so no request URL ever carries it.
/// </summary>
public sealed class YouTubePictureReader(
    IHttpClientFactory httpClients,
    IOptions<PersonaPictureOptions> options,
    ILogger<YouTubePictureReader> logger) : PersonaPictureReader(httpClients, logger)
{
    public override PersonaLinkKind Kind => PersonaLinkKind.YouTube;

    protected override async Task<PersonaPictureLookup> ReadChannelAsync(HttpClient http, string channelUrl, CancellationToken ct)
    {
        if (PersonaChannelUrl.YouTube(channelUrl) is not { } channel)
        {
            return PersonaPictureLookup.None;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://www.googleapis.com/youtube/v3/channels?part=snippet&{channel.Parameter}={Uri.EscapeDataString(channel.Value)}");
        request.Headers.Add("X-Goog-Api-Key", options.Value.YouTubeApiKey);

        var (body, failure) = await ReadJsonAsync(http, request, ct);
        if (failure is not null)
        {
            return failure;
        }

        using (body)
        {
            // An unknown channel is a success with no item, not a 404.
            if (!body!.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
            {
                return PersonaPictureLookup.None;
            }

            return PersonaPictureLookup.FromUrl(StringAt(items[0], "snippet", "thumbnails", "medium", "url"));
        }
    }
}
