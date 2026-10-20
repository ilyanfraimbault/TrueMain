using AwesomeAssertions;
using Core.Lol.Benchmarks;
using Core.Lol.Ranking;
using Data.Repositories;
using Ingestor.Processes.Components.MatchIngestion;

namespace TrueMain.UnitTests;

public sealed class FormBenchmarkBuilderTests
{
    private static FormBenchmarkBuilder.Line Line(int team, string position, int kills, int deaths, int assists, int cs = 0, int vision = 0)
        => new(team, position, kills, deaths, assists, cs, vision);

    [Fact]
    public void BuildAtTier_BinsEveryLanersFiveFigures_AgainstTheirOwnTeamsKills()
    {
        // A 30-minute game. Blue took 10 kills, red 4; the support line has no canonical lane.
        var lines = new[]
        {
            Line(100, "MIDDLE", kills: 4, deaths: 2, assists: 4, cs: 240, vision: 30),
            Line(100, "TOP", kills: 6, deaths: 0, assists: 0),
            Line(200, "BOTTOM", kills: 1, deaths: 5, assists: 1),
            Line(200, "", kills: 3, deaths: 1, assists: 0),
        };

        var keys = FormBenchmarkBuilder.BuildAtTier("16.19", 30 * 60, false, RankTier.Emerald, lines);

        keys.Should().HaveCount(3 * 5);
        keys.Should().OnlyContain(key => key.Patch == "16.19" && key.Tier == RankTier.Emerald);
        keys.Should().Contain(Key("MIDDLE", FormMetric.Kda, 40));
        keys.Should().Contain(Key("MIDDLE", FormMetric.KillParticipation, 80));
        keys.Should().Contain(Key("MIDDLE", FormMetric.CsPerMinute, 80));
        keys.Should().Contain(Key("MIDDLE", FormMetric.Deaths, 2));
        keys.Should().Contain(Key("MIDDLE", FormMetric.VisionPerMinute, 20));
        // A deathless game floors deaths at one, as the tiles do: 6 / 1.
        keys.Should().Contain(Key("TOP", FormMetric.Kda, 60));
        // Red's kills count the line with no lane: 2 of 4.
        keys.Should().Contain(Key("BOTTOM", FormMetric.KillParticipation, 50));
    }

    [Fact]
    public void BuildAtTier_SkipsARemake()
        => FormBenchmarkBuilder.BuildAtTier("16.19", 200, false, RankTier.Gold, [Line(100, "TOP", 1, 1, 1)])
            .Should().BeEmpty();

    [Fact]
    public void BuildAtTier_ReadsNoKillParticipationIntoATeamThatKilledNoOne()
        => FormBenchmarkBuilder.BuildAtTier("16.19", 20 * 60, false, RankTier.Gold, [Line(100, "TOP", 0, 3, 0)])
            .Should().Contain(Key("TOP", FormMetric.KillParticipation, 0, RankTier.Gold));

    [Fact]
    public void Count_SumsTheSameBinAcrossLaners()
    {
        var keys = new[] { Key("MIDDLE", FormMetric.Deaths, 3), Key("MIDDLE", FormMetric.Deaths, 3), Key("TOP", FormMetric.Deaths, 3) };

        FormBenchmarkBuilder.Count(keys).Should().BeEquivalentTo(new[]
        {
            new FormBenchmarkCount(Key("MIDDLE", FormMetric.Deaths, 3), 2),
            new FormBenchmarkCount(Key("TOP", FormMetric.Deaths, 3), 1),
        });
    }

    private static FormBenchmarkKey Key(string position, FormMetric metric, int bucket, RankTier tier = RankTier.Emerald)
        => new("16.19", tier, position, metric, bucket);
}
