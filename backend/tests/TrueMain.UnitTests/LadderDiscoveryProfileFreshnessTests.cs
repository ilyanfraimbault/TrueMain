using AwesomeAssertions;
using Core.Lol.Identifiers;
using Ingestor.Options;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// The apex ladder entry has carried its PUUID since #1312, so summoner-v4 adds only
/// profileIconId / summonerLevel / summonerId for an account we already store. #1358 stops
/// paying for that on an entry we saw days ago — roughly two thirds of a saturated window.
/// </summary>
public sealed class LadderDiscoveryProfileFreshnessTests
{
    private static readonly PlatformRoute Platform = PlatformId.Parse("KR").Route;

    [Fact]
    public async Task DiscoverSummonersAsync_SkipsSummonerCall_ForAnAccountWithAFreshProfile()
    {
        var client = BuildClient();
        var service = new LadderDiscoveryService(client);

        var result = await service.DiscoverSummonersAsync(
            Platform,
            Options(TimeSpan.FromDays(7)),
            offset: 0,
            Fresh("p0"),
            CancellationToken.None);

        result.ProfileCallsSkipped.Should().Be(1);
        result.Discovered.Should().HaveCount(2, "a skipped call still yields the entry, PUUID and rank included");

        var skipped = result.Discovered.Single(item => item.Summoner.Puuid == "p0");
        skipped.ProfileResolved.Should().BeFalse("nothing was read, so the upsert must not overwrite the row");
        skipped.Rank.Should().NotBeNull("the ladder entry itself carries the rank");

        await client.DidNotReceive().GetSummonerByPuuidAsync(
            Arg.Any<PlatformRoute>(), "p0", Arg.Any<CancellationToken>());
        await client.Received(1).GetSummonerByPuuidAsync(
            Arg.Any<PlatformRoute>(), "p1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DiscoverSummonersAsync_ResolvesEveryEntry_WhenFreshnessIsDisabled()
    {
        var client = BuildClient();
        var service = new LadderDiscoveryService(client);

        // Zero restores the pre-#1358 behaviour, and the probe must not even be consulted.
        var probeCalls = 0;
        var result = await service.DiscoverSummonersAsync(
            Platform,
            Options(TimeSpan.Zero),
            offset: 0,
            (_, _) =>
            {
                probeCalls++;
                return Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal) { "p0" });
            },
            CancellationToken.None);

        probeCalls.Should().Be(0);
        result.ProfileCallsSkipped.Should().Be(0);
        result.Discovered.Should().OnlyContain(item => item.ProfileResolved);
        await client.Received(2).GetSummonerByPuuidAsync(
            Arg.Any<PlatformRoute>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static ProfileFreshnessProbe Fresh(params string[] puuids)
        => (_, _) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(puuids, StringComparer.Ordinal));

    private static IRiotPlatformClient BuildClient()
    {
        var client = Substitute.For<IRiotPlatformClient>();

        var entries = Enumerable.Range(0, 2)
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

        return client;
    }

    private static DiscoveryOptions Options(TimeSpan profileSyncFreshness) => new()
    {
        TierScope = ["Challenger"],
        MaxAccountsPerPlatformPerRun = 10,
        SlidingWindowEnabled = true,
        ProfileSyncFreshness = profileSyncFreshness
    };
}
