using System.Net;
using System.Net.Http.Headers;
using Data.Entities;
using Microsoft.Extensions.Options;
using TrueMain.Options;

namespace TrueMain.Services.Personas.Pictures;

/// <summary>
/// A Twitch channel's picture, through Helix <c>users</c>, with an app access token from the client
/// credentials flow, kept until Twitch stops accepting it. A singleton: it holds that token.
/// </summary>
public sealed class TwitchPictureReader(
    IHttpClientFactory httpClients,
    IOptions<PersonaPictureOptions> options,
    ILogger<TwitchPictureReader> logger) : PersonaPictureReader(httpClients, logger)
{
    /// <summary>In the URL of the picture Twitch gives a channel that never set one.</summary>
    private const string DefaultPictureMarker = "user-default-pictures";

    private string? _token;

    public override PersonaLinkKind Kind => PersonaLinkKind.Twitch;

    protected override async Task<PersonaPictureLookup> ReadChannelAsync(HttpClient http, string channelUrl, CancellationToken ct)
    {
        if (PersonaChannelUrl.Twitch(channelUrl) is not { } login)
        {
            return PersonaPictureLookup.None;
        }

        _token ??= await RequestTokenAsync(http, ct);
        using var response = await SendAsync(http, login, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return await ReadUserAsync(response, ct);
        }

        // The token expired: one new one, one more try.
        _token = await RequestTokenAsync(http, ct);
        using var retried = await SendAsync(http, login, ct);
        return await ReadUserAsync(retried, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpClient http, string login, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"https://api.twitch.tv/helix/users?login={Uri.EscapeDataString(login)}");
        request.Headers.Add("Client-Id", options.Value.TwitchClientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return await http.SendAsync(request, ct);
    }

    private static async Task<PersonaPictureLookup> ReadUserAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            return response.StatusCode == HttpStatusCode.BadRequest ? PersonaPictureLookup.None : PersonaPictureLookup.Unavailable;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var body = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!body.RootElement.TryGetProperty("data", out var users) || users.GetArrayLength() == 0)
        {
            return PersonaPictureLookup.None;
        }

        var url = StringAt(users[0], "profile_image_url");
        return url?.Contains(DefaultPictureMarker, StringComparison.Ordinal) == true
            ? PersonaPictureLookup.None
            : PersonaPictureLookup.FromUrl(url);
    }

    private async Task<string?> RequestTokenAsync(HttpClient http, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = options.Value.TwitchClientId!,
            ["client_secret"] = options.Value.TwitchClientSecret!,
            ["grant_type"] = "client_credentials",
        });
        using var response = await http.PostAsync("https://id.twitch.tv/oauth2/token", content, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Twitch refused the app token: {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var body = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return StringAt(body.RootElement, "access_token");
    }
}
