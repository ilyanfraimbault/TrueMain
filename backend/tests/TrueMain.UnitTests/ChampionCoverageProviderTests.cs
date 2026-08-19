using AwesomeAssertions;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Components.Coverage;
using NSubstitute;

namespace TrueMain.UnitTests;

public sealed class ChampionCoverageProviderTests
{
    [Fact]
    public async Task GetSnapshotAsync_ReturnsEmpty_WhenNoMainsExist()
    {
        var snapshot = await GetSnapshotAsync([]);

        snapshot.Should().BeSameAs(ChampionCoverageSnapshot.Empty);
    }

    [Fact]
    public async Task GetSnapshotAsync_ReturnsPopulatedSnapshot_WhenMainsExist()
    {
        // GetMainCountsByPlatformAndChampionAsync runs WHERE IsMain GROUP BY PlatformId,
        // ChampionId, so a (platform, champion) pair with no mains is absent from the
        // dictionary (never a 0 count) — that absence is the real "no mains" case.
        var snapshot = await GetSnapshotAsync(
            new Dictionary<(string, int), int> { [("EUW1", 22)] = 30 },
            targetMainsPerChampion: 20);

        snapshot.Should().NotBeSameAs(ChampionCoverageSnapshot.Empty);
        snapshot.Deficit("EUW1", 266).Should().Be(1); // absent => maximal scarcity
        snapshot.Deficit("EUW1", 22).Should().Be(0);  // at/above target => no scarcity
        snapshot.Deficit("KR", 22).Should().Be(1);    // covered on EUW1 is not covered on KR
    }

    private static async Task<ChampionCoverageSnapshot> GetSnapshotAsync(
        Dictionary<(string PlatformId, int ChampionId), int> mainsByPlatformChampion,
        int targetMainsPerChampion = 20)
    {
        var repo = Substitute.For<IMainChampionStatRepository>();
        repo.GetMainCountsByPlatformAndChampionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mainsByPlatformChampion));

        var session = Substitute.For<IDataSession>();
        session.MainChampionStats.Returns(repo);

        var provider = new ChampionCoverageProvider(
            Microsoft.Extensions.Options.Options.Create(
                new CoverageOptions { TargetMainsPerChampion = targetMainsPerChampion }));

        return await provider.GetSnapshotAsync(session, CancellationToken.None);
    }
}
