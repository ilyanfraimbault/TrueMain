using AwesomeAssertions;
using TrueMain.Services.Champions.Builds;
using TrueMain.Services.Champions.Directory;

namespace TrueMain.UnitTests;

/// <summary>
/// The directory folds in memory what it used to ask Postgres or LINQ for, per slice (#2425). The
/// plain LINQ below is that older reading, kept as the oracle: the two have to agree on every line
/// of a corpus with ties, thin lanes, several populations and malformed dimensions.
/// </summary>
public sealed class ChampionDirectoryFoldParityTests
{
    private const int Seed = 2425;

    private static readonly string[] Bands = ["GOLD", "PLATINUM", "DIAMOND", "MASTER", "GRANDMASTER", "CHALLENGER", "UNRANKED"];
    private static readonly (int ChampionId, string Position)[] Lanes =
        [.. Enumerable.Range(1, 12).SelectMany(champion => new[] { (champion, "TOP"), (champion, "MIDDLE"), (champion, string.Empty) })];

    private static readonly IReadOnlyList<string>?[] Filters =
    [
        null,
        [],
        ["GOLD"],
        ["MASTER", "GRANDMASTER", "CHALLENGER"],
        ["DIAMOND", "MASTER", "GRANDMASTER", "CHALLENGER"],
        ["SILVER"],
    ];

    [Fact]
    public void TopBuildsAgreeWithTheFoldTheyReplaced()
    {
        var random = new Random(Seed);
        var builds = Enumerable.Range(0, 60).ToDictionary(_ => Guid.NewGuid(), _ => new ChampionDirectoryTopBuilds.BuildItems(
            random.Next(5) == 0 ? 0 : random.Next(1, 4),
            random.Next(1, 8), random.Next(1, 8), random.Next(0, 8), random.Next(0, 8), 0, 0));
        var runes = Enumerable.Range(0, 12).ToDictionary(_ => Guid.NewGuid(), _ => new ChampionDirectoryTopBuilds.RuneChoice(
            random.Next(6) == 0 ? 0 : random.Next(1, 4), random.Next(1, 4)));
        var missing = Guid.NewGuid();
        var buildIds = builds.Keys.Append(missing).ToArray();
        var runeIds = runes.Keys.ToArray();

        var rows = Enumerable.Range(0, 6000)
            .Select(_ => (
                Lane: Lanes[random.Next(Lanes.Length)],
                Band: Bands[random.Next(Bands.Length)],
                IsMain: random.Next(3) != 0,
                BuildId: buildIds[random.Next(buildIds.Length)],
                RuneId: runeIds[random.Next(runeIds.Length)],
                Games: random.Next(1, 6),
                Wins: random.Next(0, 3)))
            .ToList();

        var builder = new ChampionDirectoryTopBuilds.Builder();
        foreach (var row in rows)
        {
            builder.Add(row.Lane.ChampionId, row.Lane.Position, row.Band, row.IsMain, row.BuildId, row.RuneId, row.Games, row.Wins);
        }

        var subject = new ChampionDirectoryTopBuilds(builder, builds, runes);

        foreach (var filter in Filters)
        {
            foreach (var truemainsOnly in new[] { true, false })
            {
                var expected = rows
                    .Where(row => filter is null || filter.Contains(row.Band))
                    .Where(row => !truemainsOnly || row.IsMain)
                    .GroupBy(row => row.Lane)
                    .Select(lane => (lane.Key, Build: OracleTopBuild(lane
                        .GroupBy(row => (row.BuildId, row.RuneId))
                        .Select(pair => (pair.Key.BuildId, pair.Key.RuneId, Games: pair.Sum(row => row.Games), Wins: pair.Sum(row => row.Wins)))
                        .ToList(), builds, runes)))
                    .Where(line => line.Build is not null)
                    .ToDictionary(line => line.Key, line => line.Build!);

                var actual = subject.Resolve(filter, truemainsOnly);

                actual.Keys.Should().BeEquivalentTo(expected.Keys);
                foreach (var (lane, build) in expected)
                {
                    var top = actual[lane];
                    (top.FirstItemId, top.PrimaryKeystoneId, top.SecondaryStyleId).Should().Be((build.FirstItem, build.Keystone, build.SecondaryStyle));
                    top.ItemPath.Should().Equal(build.Path);
                }
            }
        }
    }

    [Fact]
    public void ScopeGroupsAgreeWithTheGroupByTheyReplaced()
    {
        var random = new Random(Seed);
        var accounts = Enumerable.Range(0, 40).Select(_ => Guid.NewGuid()).ToArray();
        var start = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, 4000)
            .Select(_ => (
                Lane: Lanes[random.Next(Lanes.Length)],
                Band: Bands[random.Next(Bands.Length)],
                IsMain: random.Next(3) != 0,
                Account: accounts[random.Next(accounts.Length)],
                Games: random.Next(1, 30),
                Wins: random.Next(0, 15),
                At: start.AddMinutes(random.Next(5000))))
            .ToList();

        var builder = new ChampionDirectoryScopeGroups.Builder();
        foreach (var row in rows)
        {
            builder.Add(row.Lane.ChampionId, row.Lane.Position, row.Band, row.IsMain, row.Account, row.Games, row.Wins, row.At);
        }

        var subject = new ChampionDirectoryScopeGroups(builder);

        foreach (var filter in Filters)
        {
            foreach (var truemainsOnly in new[] { true, false })
            {
                var expected = rows
                    .Where(row => filter is null || filter.Contains(row.Band))
                    .Where(row => !truemainsOnly || row.IsMain)
                    .GroupBy(row => row.Lane)
                    .Select(lane => new ChampionDirectoryScopeGroups.Group(
                        lane.Key.ChampionId,
                        lane.Key.Position,
                        lane.Sum(row => row.Games),
                        lane.Sum(row => row.Wins),
                        lane.Where(row => row.IsMain).Select(row => row.Account).Distinct().Count(),
                        lane.Max(row => row.At)))
                    .ToList();

                subject.Resolve(filter, truemainsOnly).Should().BeEquivalentTo(expected);
            }
        }
    }

    private static OracleBuild? OracleTopBuild(
        List<(Guid BuildId, Guid RuneId, int Games, int Wins)> pairs,
        Dictionary<Guid, ChampionDirectoryTopBuilds.BuildItems> builds,
        Dictionary<Guid, ChampionDirectoryTopBuilds.RuneChoice> runes)
    {
        var enriched = pairs
            .Where(pair => builds.TryGetValue(pair.BuildId, out var build) && build.BuildItem0 > 0
                && runes.TryGetValue(pair.RuneId, out var rune) && rune.PrimaryKeystoneId > 0)
            .Select(pair => (Build: builds[pair.BuildId], Rune: runes[pair.RuneId], pair.Games, pair.Wins))
            .ToList();
        if (enriched.Count == 0)
        {
            return null;
        }

        var top = enriched
            .GroupBy(row => (row.Build.BuildItem0, row.Rune.PrimaryKeystoneId))
            .OrderByDescending(bucket => bucket.Sum(row => row.Games))
            .ThenBy(bucket => bucket.Key.BuildItem0)
            .ThenBy(bucket => bucket.Key.PrimaryKeystoneId)
            .First();
        var games = top.Sum(row => row.Games);
        var tree = ChampionBuildPathAnalyzer.BuildItemTree(
            [.. top.Select(row => new ChampionBuildPathAnalyzer.BuildSequence(
                row.Build.BuildItem1, row.Build.BuildItem2, row.Build.BuildItem3,
                row.Build.BuildItem4, row.Build.BuildItem5, row.Build.BuildItem6,
                row.Games, row.Wins))],
            games);
        var (path, _, _) = ChampionBuildPathAnalyzer.WalkPath(tree, top.Key.BuildItem0, games, top.Sum(row => row.Wins));
        var secondary = top
            .GroupBy(row => row.Rune.SecondaryStyleId)
            .OrderByDescending(group => group.Sum(row => row.Games))
            .ThenBy(group => group.Key)
            .First().Key;
        return new OracleBuild(top.Key.BuildItem0, top.Key.PrimaryKeystoneId, secondary, path);
    }

    private sealed record OracleBuild(int FirstItem, int Keystone, int SecondaryStyle, IReadOnlyList<int> Path);
}
