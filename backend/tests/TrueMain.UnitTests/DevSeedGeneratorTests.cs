using AwesomeAssertions;
using Data;
using Data.Entities;
using DevSeed;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.UnitTests;

/// <summary>
/// Pins the DevSeed generator's internal consistency: the matchup rows it writes are folded from the
/// very games it generates, so the live and pre-aggregated reads of a seeded database agree, and every
/// patch and elo bracket the champion page filters on carries a build aggregate.
/// </summary>
public sealed class DevSeedGeneratorTests
{
    private const string CurrentPatch = "16.3";
    private const int PatchCount = 3;
    private const int GamesPerPatch = 24;

    private static readonly DateTime NowUtc = new(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Generate_EmitsEveryGameWithBothLaneSides()
    {
        using var db = CreateContext();
        var result = Generate(db);

        result.Matches.Should().HaveCount(PatchCount * GamesPerPatch);
        result.Matches.Select(match => match.Id).Should().OnlyHaveUniqueItems();
        result.Participants.Should().HaveCount(2 * result.Matches.Count);
        result.Participants.GroupBy(participant => participant.MatchId)
            .Should().AllSatisfy(game => game.Count(participant => participant.Win).Should().Be(1));
    }

    [Fact]
    public void Generate_FoldsTheMatchupRowsFromTheSameGames()
    {
        using var db = CreateContext();
        var self = ChampionArchetypes.Seeds[0];
        var result = Generate(db, self);

        var selfSides = result.Participants.Where(participant => participant.ChampionId == self.Id).ToList();
        var gamesWithLaneSnapshot = result.Snapshots
            .Where(snapshot => snapshot.ParticipantId == 1 && snapshot.IntervalMinute == 15)
            .Select(snapshot => snapshot.MatchId)
            .Distinct()
            .Count();

        result.MatchupStats.Sum(row => row.Games).Should().Be(result.Matches.Count);
        result.MatchupStats.Sum(row => row.Wins).Should().Be(selfSides.Count(participant => participant.Win));
        result.MatchupStats.Sum(row => row.LaneGames).Should().Be(gamesWithLaneSnapshot);
        result.MatchupStats.Should().AllSatisfy(row =>
        {
            (row.LaneWins + row.LaneLosses).Should().BeLessThanOrEqualTo(row.LaneGames);
            row.LaneGoldDiffGames.Should().Be(row.LaneGames);
            row.LaneXpDiffGames.Should().Be(row.LaneGames);
        });
    }

    [Fact]
    public void Generate_WritesTwoBuildPatternsPerPatchAndBracket()
    {
        using var db = CreateContext();
        var result = Generate(db);

        result.Scopes.Select(scope => scope.GameVersion).Distinct()
            .Should().BeEquivalentTo(["16.1", "16.2", CurrentPatch]);
        result.Scopes.Should().HaveCount(PatchCount * 4);
        result.Patterns.Should().HaveCount(2 * result.Scopes.Count);
        result.Scopes.Should().AllSatisfy(scope =>
            result.Patterns.Count(pattern => pattern.ScopeId == scope.Id).Should().Be(2));
    }

    [Fact]
    public void Generate_IsDeterministicPerChampion()
    {
        using var db = CreateContext();
        var first = Generate(db);
        var second = Generate(db);

        second.Participants.Select(participant => (participant.ChampionId, participant.Win, participant.Kills))
            .Should().Equal(first.Participants.Select(participant => (participant.ChampionId, participant.Win, participant.Kills)));
        second.Matches.Select(match => match.GameDurationSeconds)
            .Should().Equal(first.Matches.Select(match => match.GameDurationSeconds));
    }

    private static SeedGenerator.GenerationResult Generate(TrueMainDbContext db, ChampionSeed? self = null)
    {
        self ??= ChampionArchetypes.Seeds[0];
        var opponents = ChampionArchetypes.Seeds
            .Where(seed => seed.Position == self.Position && seed.Id != self.Id)
            .ToList();
        var account = new RiotAccount
        {
            Id = Guid.NewGuid(),
            Puuid = "devseed-test",
            PlatformId = "EUW1",
            GameName = "DevSeed",
        };

        var generator = new SeedGenerator(account, new DimCache(db), CurrentPatch, PatchCount, GamesPerPatch);
        return generator.Generate(self, opponents, NowUtc);
    }

    // The dimension cache only adds rows to the context; nothing is saved, so no connection is ever
    // opened — UseNpgsql only selects the provider.
    private static TrueMainDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TrueMainDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only")
            .Options);
}
