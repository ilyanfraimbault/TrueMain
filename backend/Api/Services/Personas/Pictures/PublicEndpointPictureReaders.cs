using System.Net;
using Data.Entities;

namespace TrueMain.Services.Personas.Pictures;

/// <summary>A Kick channel's picture, from the public channel endpoint.</summary>
public sealed class KickPictureReader(IHttpClientFactory httpClients, ILogger<KickPictureReader> logger)
    : PersonaPictureReader(httpClients, logger)
{
    public override PersonaLinkKind Kind => PersonaLinkKind.Kick;

    protected override async Task<PersonaPictureLookup> ReadChannelAsync(HttpClient http, string channelUrl, CancellationToken ct)
    {
        if (PersonaChannelUrl.Kick(channelUrl) is not { } slug)
        {
            return PersonaPictureLookup.None;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://kick.com/api/v2/channels/{slug}");
        var (body, failure) = await ReadJsonAsync(http, request, ct);
        if (failure is not null)
        {
            return failure;
        }

        using (body)
        {
            return PersonaPictureLookup.FromUrl(StringAt(body!.RootElement, "user", "profile_pic"));
        }
    }
}

/// <summary>A Chzzk channel's picture, from the public channel endpoint.</summary>
public sealed class ChzzkPictureReader(IHttpClientFactory httpClients, ILogger<ChzzkPictureReader> logger)
    : PersonaPictureReader(httpClients, logger)
{
    public override PersonaLinkKind Kind => PersonaLinkKind.Chzzk;

    protected override async Task<PersonaPictureLookup> ReadChannelAsync(HttpClient http, string channelUrl, CancellationToken ct)
    {
        if (PersonaChannelUrl.Chzzk(channelUrl) is not { } id)
        {
            return PersonaPictureLookup.None;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.chzzk.naver.com/service/v1/channels/{id}");
        var (body, failure) = await ReadJsonAsync(http, request, ct);
        if (failure is not null)
        {
            return failure;
        }

        using (body)
        {
            return PersonaPictureLookup.FromUrl(StringAt(body!.RootElement, "content", "channelImageUrl"));
        }
    }
}

/// <summary>
/// A SOOP channel's picture. Its image host answers every id: a JPEG for a channel's own picture, a
/// PNG placeholder for one without. So the header alone says whether there is a picture to show.
/// </summary>
public sealed class SoopPictureReader(IHttpClientFactory httpClients, ILogger<SoopPictureReader> logger)
    : PersonaPictureReader(httpClients, logger)
{
    public override PersonaLinkKind Kind => PersonaLinkKind.Soop;

    protected override async Task<PersonaPictureLookup> ReadChannelAsync(HttpClient http, string channelUrl, CancellationToken ct)
    {
        if (PersonaChannelUrl.Soop(channelUrl) is not { } id || id.Length < 2)
        {
            return PersonaPictureLookup.None;
        }

        var url = $"https://profile.img.sooplive.co.kr/LOGO/{id[..2]}/{id}/{id}.jpg";
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        using var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
        {
            return response.Content.Headers.ContentType?.MediaType == "image/jpeg"
                ? PersonaPictureLookup.FromUrl(url)
                : PersonaPictureLookup.None;
        }

        return response.StatusCode == HttpStatusCode.NotFound ? PersonaPictureLookup.None : PersonaPictureLookup.Unavailable;
    }
}
