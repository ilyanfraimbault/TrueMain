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
        var snapshot = await GetSnapshotAsync(new Dictionary<int, int>());

        snapshot.Should().BeSameAs(ChampionCoverageSnapshot.Empty);
    }

    [Fact]
    public async Task GetSnapshotAsync_ReturnsPopulatedSnapshot_WhenMainsExist()
    {
        // GetMainCountsByChampionAsync runs WHERE IsMain GROUP BY ChampionId, so a champion
        // with no mains is absent from the dictionary (never a 0 count) — that absence is the
        // real "no mains" case.
        var snapshot = await GetSnapshotAsync(
            new Dictionary<int, int> { [22] = 30 },
            targetMainsPerChampion: 20);

        snapshot.Should().NotBeSameAs(ChampionCoverageSnapshot.Empty);
        snapshot.Deficit(266).Should().Be(1); // absent from the IsMain result => maximal scarcity
        snapshot.Deficit(22).Should().Be(0);  // at/above target => no scarcity
    }

    private static async Task<ChampionCoverageSnapshot> GetSnapshotAsync(
        Dictionary<int, int> mainsByChampion,
        int targetMainsPerChampion = 20)
    {
        var repo = Substitute.For<IMainChampionStatRepository>();
        repo.GetMainCountsByChampionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(mainsByChampion));

        var session = Substitute.For<IDataSession>();
        session.MainChampionStats.Returns(repo);

        var provider = new ChampionCoverageProvider(
            Microsoft.Extensions.Options.Options.Create(
                new CoverageOptions { TargetMainsPerChampion = targetMainsPerChampion }));

        return await provider.GetSnapshotAsync(session, CancellationToken.None);
    }
}
