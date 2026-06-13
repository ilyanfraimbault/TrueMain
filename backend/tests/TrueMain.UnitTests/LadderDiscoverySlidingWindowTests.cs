using AwesomeAssertions;
using Core.Lol.Identifiers;
using Ingestor.Options;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using NSubstitute;

namespace TrueMain.UnitTests;

public sealed class LadderDiscoverySlidingWindowTests
{
    private static readonly PlatformRoute Platform = PlatformId.Parse("KR").Route;

    [Theory]
    [InlineData(0, new[] { "p0", "p1" })]
    [InlineData(2, new[] { "p2", "p3" })]
    [InlineData(4, new[] { "p4" })]
    public async Task DiscoverSummonersAsync_SlidesWindowByOffset(int offset, string[] expectedPuuids)
    {
        var service = BuildService(ladderSize: 5);
        var options = Options(window: 2, slidingEnabled: true);

        var result = await service.DiscoverSummonersAsync(Platform, options, offset, CancellationToken.None);

        result.LadderSize.Should().Be(5);
        result.AppliedOffset.Should().Be(offset);
        result.Discovered.Select(d => d.Summoner.Puuid).Should().Equal(expectedPuuids);
    }

    [Fact]
    public async Task DiscoverSummonersAsync_WrapsStaleOffsetWithModulo()
    {
        var service = BuildService(ladderSize: 5);
        var options = Options(window: 2, slidingEnabled: true);

        // Offset == ladder size: modulo wraps back to the top instead of returning nothing.
        var result = await service.DiscoverSummonersAsync(Platform, options, offset: 5, CancellationToken.None);

        result.AppliedOffset.Should().Be(0);
        result.Discovered.Select(d => d.Summoner.Puuid).Should().Equal("p0", "p1");
    }

    [Fact]
    public async Task DiscoverSummonersAsync_WhenSlidingDisabled_AlwaysStartsAtTop()
    {
        var service = BuildService(ladderSize: 5);
        var options = Options(window: 2, slidingEnabled: false);

        var result = await service.DiscoverSummonersAsync(Platform, options, offset: 3, CancellationToken.None);

        result.AppliedOffset.Should().Be(0);
        result.Discovered.Select(d => d.Summoner.Puuid).Should().Equal("p0", "p1");
    }

    private static LadderDiscoveryService BuildService(int ladderSize)
    {
        var client = Substitute.For<IRiotPlatformClient>();

        var entries = Enumerable.Range(0, ladderSize)
            .Select(i => new RiotLeagueEntryDto
            {
                Puuid = $"p{i}",
                Rank = "I",
                LeaguePoints = 100 - i
            })
            .ToList();

        client.GetChallengerLeagueAsync(Arg.Any<PlatformRoute>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RiotLeagueListDto { Tier = "CHALLENGER", Entries = entries }));

        client.GetSummonerByPuuidAsync(Arg.Any<PlatformRoute>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new RiotSummonerDto { Puuid = call.ArgAt<string>(1) }));

        return new LadderDiscoveryService(client);
    }

    private static DiscoveryOptions Options(int window, bool slidingEnabled) => new()
    {
        TierScope = ["Challenger"],
        MaxAccountsPerPlatformPerRun = window,
        SlidingWindowEnabled = slidingEnabled
    };
}
