using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;
using NSubstitute;
using TrueMain.Options;
using TrueMain.Services.Personas.Pictures;

namespace TrueMain.UnitTests;

/// <summary>How each platform's answer reads as a picture (#2446), and what a stored link names.</summary>
public sealed class PersonaPictureReaderTests
{
    private static readonly PersonaPictureOptions Credentials = new()
    {
        YouTubeApiKey = "yt-key",
        TwitchClientId = "twitch-id",
        TwitchClientSecret = "twitch-secret",
    };

    [Theory]
    [InlineData("https://www.youtube.com/@Faker", "forHandle", "@Faker")]
    [InlineData("https://www.youtube.com/@%ED%9D%B0%EB%B9%84", "forHandle", "@흰비")]
    [InlineData("https://www.youtube.com/channel/UCVOHlrXEahRwNX0o6DVB4vQ", "id", "UCVOHlrXEahRwNX0o6DVB4vQ")]
    [InlineData("https://www.youtube.com/user/faker", "forUsername", "faker")]
    public void YouTubeLink_NamesItsChannel(string url, string parameter, string value)
    {
        PersonaChannelUrl.YouTube(url).Should().Be((parameter, value));
    }

    [Theory]
    [InlineData("https://www.youtube.com/c/faker")]
    [InlineData("https://www.youtube.com/faker")]
    [InlineData("https://www.youtube.com/@")]
    [InlineData("https://www.youtube.com/channel/notachannel")]
    [InlineData("not a url")]
    public void YouTubeLink_ThatTheApiCannotLookUp_NamesNothing(string url)
    {
        PersonaChannelUrl.YouTube(url).Should().BeNull();
    }

    [Theory]
    [InlineData("https://www.twitch.tv/keria", "keria")]
    [InlineData("https://www.twitch.tv/not%20a%20login", null)]
    [InlineData("https://www.twitch.tv/keria/videos", null)]
    public void TwitchLink_NamesItsLogin(string url, string? login)
    {
        PersonaChannelUrl.Twitch(url).Should().Be(login);
    }

    [Theory]
    [InlineData("https://www.sooplive.co.kr/station/zxc582586", "zxc582586")]
    [InlineData("https://www.sooplive.co.kr/zxc582586", null)]
    [InlineData("https://www.sooplive.co.kr/station/..%2Fetc", null)]
    public void SoopLink_NamesItsStation(string url, string? id)
    {
        PersonaChannelUrl.Soop(url).Should().Be(id);
    }

    [Fact]
    public async Task YouTube_ReadsTheMediumThumbnail_AndSendsTheKeyInAHeaderNotTheUrl()
    {
        using var handler = new FakeHandler(_ => Json("""
            {"items":[{"snippet":{"thumbnails":{"medium":{"url":"https://yt3.ggpht.com/picture=s240"}}}}]}
            """));

        var lookup = await YouTube(handler).ReadAsync("https://www.youtube.com/@Faker", CancellationToken.None);

        lookup.Should().Be(new PersonaPictureLookup(PersonaPictureOutcome.Found, "https://yt3.ggpht.com/picture=s240"));
        var request = handler.Requests.Single();
        request.RequestUri!.Query.Should().Be("?part=snippet&forHandle=%40Faker");
        request.Headers.GetValues("X-Goog-Api-Key").Should().Equal("yt-key");
    }

    [Fact]
    public async Task YouTube_UnknownChannel_IsNoPicture()
    {
        using var handler = new FakeHandler(_ => Json("""{"pageInfo":{"totalResults":0}}"""));

        var lookup = await YouTube(handler).ReadAsync("https://www.youtube.com/@gone", CancellationToken.None);

        lookup.Should().Be(PersonaPictureLookup.None);
    }

    [Fact]
    public async Task YouTube_LegacyLink_AsksNothing()
    {
        using var handler = new FakeHandler(_ => throw new InvalidOperationException("no request expected"));

        var lookup = await YouTube(handler).ReadAsync("https://www.youtube.com/c/faker", CancellationToken.None);

        lookup.Should().Be(PersonaPictureLookup.None);
        handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)] // a revoked or mistyped key
    [InlineData(HttpStatusCode.Forbidden)] // the daily quota
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AFailedRead_IsUnavailable_NotNoPicture(HttpStatusCode status)
    {
        using var handler = new FakeHandler(_ => new HttpResponseMessage(status));

        var lookup = await YouTube(handler).ReadAsync("https://www.youtube.com/@Faker", CancellationToken.None);

        lookup.Should().Be(PersonaPictureLookup.Unavailable);
    }

    [Fact]
    public async Task ATimeout_IsUnavailable()
    {
        using var handler = new FakeHandler(_ => throw new TaskCanceledException("timed out"));

        var lookup = await YouTube(handler).ReadAsync("https://www.youtube.com/@Faker", CancellationToken.None);

        lookup.Should().Be(PersonaPictureLookup.Unavailable);
    }

    [Fact]
    public async Task APictureFromAnUnknownHost_IsNoPicture()
    {
        using var handler = new FakeHandler(_ => Json("""
            {"items":[{"snippet":{"thumbnails":{"medium":{"url":"https://elsewhere.example/picture"}}}}]}
            """));

        var lookup = await YouTube(handler).ReadAsync("https://www.youtube.com/@Faker", CancellationToken.None);

        lookup.Should().Be(PersonaPictureLookup.None);
    }

    [Fact]
    public async Task Twitch_RequestsOneToken_AndReadsTheUser()
    {
        using var handler = new FakeHandler(request => request.RequestUri!.Host == "id.twitch.tv"
            ? Json("""{"access_token":"token-1"}""")
            : Json("""{"data":[{"profile_image_url":"https://static-cdn.jtvnw.net/jtv_user_pictures/keria-300x300.png"}]}"""));
        var reader = Twitch(handler);

        await reader.ReadAsync("https://www.twitch.tv/keria", CancellationToken.None);
        var lookup = await reader.ReadAsync("https://www.twitch.tv/keria", CancellationToken.None);

        lookup.Url.Should().Be("https://static-cdn.jtvnw.net/jtv_user_pictures/keria-300x300.png");
        handler.Requests.Count(r => r.RequestUri!.Host == "id.twitch.tv").Should().Be(1);
        var read = handler.Requests.Last();
        read.Headers.GetValues("Client-Id").Should().Equal("twitch-id");
        read.Headers.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", "token-1"));
    }

    [Fact]
    public async Task Twitch_ARefusedToken_IsReplacedOnce()
    {
        var tokens = 0;
        using var handler = new FakeHandler(request =>
        {
            if (request.RequestUri!.Host == "id.twitch.tv")
            {
                return Json($$"""{"access_token":"token-{{++tokens}}"}""");
            }

            return request.Headers.Authorization!.Parameter == "token-1"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : Json("""{"data":[{"profile_image_url":"https://static-cdn.jtvnw.net/jtv_user_pictures/keria.png"}]}""");
        });

        var lookup = await Twitch(handler).ReadAsync("https://www.twitch.tv/keria", CancellationToken.None);

        lookup.Url.Should().Be("https://static-cdn.jtvnw.net/jtv_user_pictures/keria.png");
        tokens.Should().Be(2);
    }

    [Fact]
    public async Task Twitch_TheDefaultPicture_IsNoPicture()
    {
        using var handler = new FakeHandler(request => request.RequestUri!.Host == "id.twitch.tv"
            ? Json("""{"access_token":"token"}""")
            : Json("""{"data":[{"profile_image_url":"https://static-cdn.jtvnw.net/user-default-pictures-uv/x-300x300.png"}]}"""));

        var lookup = await Twitch(handler).ReadAsync("https://www.twitch.tv/keria", CancellationToken.None);

        lookup.Should().Be(PersonaPictureLookup.None);
    }

    [Fact]
    public async Task Twitch_UnknownLogin_IsNoPicture()
    {
        using var handler = new FakeHandler(request => request.RequestUri!.Host == "id.twitch.tv"
            ? Json("""{"access_token":"token"}""")
            : Json("""{"data":[]}"""));

        var lookup = await Twitch(handler).ReadAsync("https://www.twitch.tv/gone", CancellationToken.None);

        lookup.Should().Be(PersonaPictureLookup.None);
    }

    [Fact]
    public async Task Twitch_ARefusedRequest_IsUnavailable_NotNoPicture()
    {
        using var handler = new FakeHandler(request => request.RequestUri!.Host == "id.twitch.tv"
            ? Json("""{"access_token":"token"}""")
            : new HttpResponseMessage(HttpStatusCode.BadRequest));

        var lookup = await Twitch(handler).ReadAsync("https://www.twitch.tv/keria", CancellationToken.None);

        lookup.Should().Be(PersonaPictureLookup.Unavailable);
    }

    [Fact]
    public async Task Kick_UnknownChannel_IsNoPicture()
    {
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var lookup = await new KickPictureReader(Clients(handler), NullLogger<KickPictureReader>.Instance)
            .ReadAsync("https://kick.com/gone", CancellationToken.None);

        lookup.Should().Be(PersonaPictureLookup.None);
    }

    [Fact]
    public async Task Kick_ReadsTheUsersPicture()
    {
        using var handler = new FakeHandler(_ => Json("""{"user":{"profile_pic":"https://files.kick.com/images/user/1/picture.webp"}}"""));

        var lookup = await new KickPictureReader(Clients(handler), NullLogger<KickPictureReader>.Instance)
            .ReadAsync("https://kick.com/momochilol", CancellationToken.None);

        lookup.Url.Should().Be("https://files.kick.com/images/user/1/picture.webp");
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/api/v2/channels/momochilol");
    }

    [Fact]
    public async Task Kick_NoPicture_IsNoPicture()
    {
        using var handler = new FakeHandler(_ => Json("""{"user":{"profile_pic":null}}"""));

        var lookup = await new KickPictureReader(Clients(handler), NullLogger<KickPictureReader>.Instance)
            .ReadAsync("https://kick.com/momochilol", CancellationToken.None);

        lookup.Should().Be(PersonaPictureLookup.None);
    }

    [Fact]
    public async Task Chzzk_ReadsTheChannelImage()
    {
        using var handler = new FakeHandler(_ => Json("""{"content":{"channelImageUrl":"https://nng-phinf.pstatic.net/picture.png"}}"""));

        var lookup = await new ChzzkPictureReader(Clients(handler), NullLogger<ChzzkPictureReader>.Instance)
            .ReadAsync("https://chzzk.naver.com/e529cb7b8c7aee248ab15d7e6b392891", CancellationToken.None);

        lookup.Url.Should().Be("https://nng-phinf.pstatic.net/picture.png");
    }

    [Theory]
    [InlineData("image/jpeg", PersonaPictureOutcome.Found)]
    [InlineData("image/png", PersonaPictureOutcome.None)]
    public async Task Soop_OnlyAJpegIsTheChannelsOwnPicture(string contentType, PersonaPictureOutcome outcome)
    {
        using var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            return response;
        });

        var lookup = await new SoopPictureReader(Clients(handler), NullLogger<SoopPictureReader>.Instance)
            .ReadAsync("https://www.sooplive.co.kr/station/phonics1", CancellationToken.None);

        lookup.Outcome.Should().Be(outcome);
        handler.Requests.Single().RequestUri!.ToString()
            .Should().Be("https://profile.img.sooplive.co.kr/LOGO/ph/phonics1/phonics1.jpg");
    }

    private static YouTubePictureReader YouTube(FakeHandler handler) =>
        new(Clients(handler), MicrosoftOptions.Create(Credentials), NullLogger<YouTubePictureReader>.Instance);

    private static TwitchPictureReader Twitch(FakeHandler handler) =>
        new(Clients(handler), MicrosoftOptions.Create(Credentials), NullLogger<TwitchPictureReader>.Instance);

    private static IHttpClientFactory Clients(FakeHandler handler)
    {
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        return clients;
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }
}
