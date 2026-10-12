using System.Runtime.CompilerServices;
using System.Text.Json;
using AwesomeAssertions;
using Data.ItemContext;

namespace TrueMain.UnitTests;

/// <summary>
/// Holds <see cref="DraftAxisEvaluator"/> to the fixture the desktop draft's team damage bar
/// is tested against too (#1907, <c>desktop/app/tests/draft-damage.test.ts</c>). The bar the
/// player sees in champion select and the damage axes behind the build advice read the same
/// team; if either side's arithmetic or bands move, one of the two tests fails rather than
/// the two silently disagreeing.
/// </summary>
public sealed class TeamDamageShareFixtureTests
{
    private const double Tolerance = 1e-9;

    private static readonly DraftAxisThresholds Thresholds = new();

    [Fact]
    public void TheEvaluatorReadsEveryFixtureTeamAsTheDesktopBarDoes()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        var cases = fixture.RootElement.GetProperty("cases").EnumerateArray().ToList();
        cases.Should().NotBeEmpty();

        foreach (var entry in cases)
        {
            var name = entry.GetProperty("name").GetString() ?? string.Empty;
            var picks = entry.GetProperty("picks").EnumerateArray()
                .Select((pick, index) => Facts(
                    index + 1,
                    pick.GetProperty("physicalShare").GetDouble(),
                    pick.GetProperty("magicShare").GetDouble(),
                    pick.GetProperty("damagePerGame").GetDouble()))
                .ToList();
            var side = new DraftSide(picks, entry.GetProperty("missing").GetInt32());

            var axes = DraftAxisEvaluator.Evaluate(new DraftContext(side, side, null, null), Thresholds);
            var expected = entry.GetProperty("expected");

            if (expected.ValueKind == JsonValueKind.Null)
            {
                axes.Should().NotContainKey(ItemContextAxis.EnemyMagicDamage, name);
                axes.Should().NotContainKey(ItemContextAxis.AllyMagicDamage, name);
                continue;
            }

            DraftAxisEvaluator.DamageWeightedShare(picks, facts => facts.PhysicalShare)
                .Should().BeApproximately(expected.GetProperty("physicalShare").GetDouble(), Tolerance, name);
            DraftAxisEvaluator.DamageWeightedShare(picks, facts => facts.MagicShare)
                .Should().BeApproximately(expected.GetProperty("magicShare").GetDouble(), Tolerance, name);

            axes[ItemContextAxis.EnemyMagicDamage].ToString()
                .Should().Be(expected.GetProperty("enemyMagicDamage").GetString(), name);
            axes[ItemContextAxis.EnemyPhysicalDamage].ToString()
                .Should().Be(expected.GetProperty("enemyPhysicalDamage").GetString(), name);
            axes[ItemContextAxis.AllyMagicDamage].ToString()
                .Should().Be(expected.GetProperty("allyMagicDamage").GetString(), name);
        }
    }

    private static string FixturePath()
    {
        // <repo>/backend/tests/TrueMain.UnitTests/TeamDamageShareFixtureTests.cs
        var testsProject = Path.GetDirectoryName(ThisFilePath())!;
        return Path.Combine(testsProject, "Fixtures", "team-damage-share.json");
    }

    private static string ThisFilePath([CallerFilePath] string path = "") => path;

    private static ChampionProfileFacts Facts(int championId, double physical, double magic, double damagePerGame)
        => new()
        {
            ChampionId = championId,
            Position = "MIDDLE",
            Games = 1_000,
            DamagePerGame = damagePerGame,
            MagicShare = magic,
            PhysicalShare = physical,
            SustainPerMinute = 0d,
            CrowdControlPerMinute = 0d,
            TankRate = 0d,
            CritRate = 0d,
            ArmorPenetrationRate = 0d,
            IsRanged = false,
            GoldLeadAt10 = 0d,
        };
}
