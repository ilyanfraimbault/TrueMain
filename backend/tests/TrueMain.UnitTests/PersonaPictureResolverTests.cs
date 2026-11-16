using AwesomeAssertions;
using Data.Entities;
using TrueMain.Services.Personas.Pictures;

namespace TrueMain.UnitTests;

/// <summary>A known player's picture is their first channel's that has one (#2446).</summary>
public sealed class PersonaPictureResolverTests
{
    private const string YouTubePicture = "https://yt3.ggpht.com/youtube";
    private const string TwitchPicture = "https://static-cdn.jtvnw.net/twitch";

    [Fact]
    public async Task YouTube_WinsOverTwitch_WhateverOrderTheLinksComeIn()
    {
        var resolver = Resolver(new()
        {
            ["https://twitch"] = Found(TwitchPicture),
            ["https://youtube"] = Found(YouTubePicture),
        });

        var resolution = await resolver.ResolveAsync(
            [new(PersonaLinkKind.Twitch, "https://twitch"), new(PersonaLinkKind.YouTube, "https://youtube")],
            CancellationToken.None);

        resolution.Should().Be(new PersonaPictureResolution(YouTubePicture, Unsettled: false));
    }

    [Fact]
    public async Task ADeadYouTubeChannel_FallsThroughToTwitch_AsSettled()
    {
        var resolver = Resolver(new()
        {
            ["https://youtube"] = PersonaPictureLookup.None,
            ["https://twitch"] = Found(TwitchPicture),
        });

        var resolution = await resolver.ResolveAsync(
            [new(PersonaLinkKind.YouTube, "https://youtube"), new(PersonaLinkKind.Twitch, "https://twitch")],
            CancellationToken.None);

        resolution.Should().Be(new PersonaPictureResolution(TwitchPicture, Unsettled: false));
    }

    [Fact]
    public async Task AnUnreadableYouTubeChannel_FallsThroughToTwitch_AsUnsettled()
    {
        var resolver = Resolver(new()
        {
            ["https://youtube"] = PersonaPictureLookup.Unavailable,
            ["https://twitch"] = Found(TwitchPicture),
        });

        var resolution = await resolver.ResolveAsync(
            [new(PersonaLinkKind.YouTube, "https://youtube"), new(PersonaLinkKind.Twitch, "https://twitch")],
            CancellationToken.None);

        resolution.Should().Be(new PersonaPictureResolution(TwitchPicture, Unsettled: true));
    }

    [Fact]
    public async Task AnUnreadableChannelBelowTheAnswer_IsNeverAsked()
    {
        var resolver = Resolver(new()
        {
            ["https://youtube"] = Found(YouTubePicture),
            ["https://twitch"] = PersonaPictureLookup.Unavailable,
        });

        var resolution = await resolver.ResolveAsync(
            [new(PersonaLinkKind.Twitch, "https://twitch"), new(PersonaLinkKind.YouTube, "https://youtube")],
            CancellationToken.None);

        resolution.Unsettled.Should().BeFalse();
    }

    [Fact]
    public async Task WithinAKind_TheFirstChannelListedIsAskedFirst()
    {
        var resolver = Resolver(new()
        {
            ["https://new"] = PersonaPictureLookup.None,
            ["https://old"] = Found(YouTubePicture),
        });

        var resolution = await resolver.ResolveAsync(
            [new(PersonaLinkKind.YouTube, "https://new"), new(PersonaLinkKind.YouTube, "https://old")],
            CancellationToken.None);

        resolution.Url.Should().Be(YouTubePicture);
    }

    [Fact]
    public async Task ChannelsOutsideThePriority_GiveNoPicture()
    {
        var resolver = Resolver([]);

        var resolution = await resolver.ResolveAsync(
            [new(PersonaLinkKind.X, "https://x"), new(PersonaLinkKind.Leaguepedia, "https://wiki")],
            CancellationToken.None);

        resolution.Should().Be(new PersonaPictureResolution(null, Unsettled: false));
    }

    [Fact]
    public void Priority_IsYouTubeTwitchKickSoopChzzk()
    {
        PersonaPictureResolver.Priority.Should().Equal(
            PersonaLinkKind.YouTube, PersonaLinkKind.Twitch, PersonaLinkKind.Kick, PersonaLinkKind.Soop, PersonaLinkKind.Chzzk);
    }

    private static PersonaPictureLookup Found(string url) => new(PersonaPictureOutcome.Found, url);

    private static PersonaPictureResolver Resolver(Dictionary<string, PersonaPictureLookup> answers) =>
        new(PersonaPictureResolver.Priority.Select(kind => new FakeReader(kind, answers)));

    private sealed class FakeReader(PersonaLinkKind kind, Dictionary<string, PersonaPictureLookup> answers) : IPersonaPictureReader
    {
        public PersonaLinkKind Kind => kind;

        public Task<PersonaPictureLookup> ReadAsync(string channelUrl, CancellationToken ct) => Task.FromResult(answers[channelUrl]);
    }
}
