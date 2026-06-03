using AwesomeAssertions;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

public sealed class ChampionAggregateScopeResolverTests
{
    [Fact]
    public void ResolvePreviousPatchVersion_returns_the_next_most_recent_below_active()
    {
        var versions = new[] { "16.5", "16.4", "16.3", "15.24" };

        var previous = ChampionAggregateScopeResolver.ResolvePreviousPatchVersion(versions, "16.5");

        previous.Should().Be("16.4", "16.4 is the highest patch strictly below the active 16.5");
    }

    [Fact]
    public void ResolvePreviousPatchVersion_crosses_the_major_boundary()
    {
        var versions = new[] { "16.1", "15.24", "15.23" };

        var previous = ChampionAggregateScopeResolver.ResolvePreviousPatchVersion(versions, "16.1");

        previous.Should().Be("15.24", "the previous patch is the latest of the prior major when the active is x.1");
    }

    [Fact]
    public void ResolvePreviousPatchVersion_ignores_patches_at_or_above_active()
    {
        // A later patch present in the set (e.g. an early-ingest 16.6 row while
        // 16.5 is still the resolved active patch) must never be picked as the
        // "previous" patch — only strictly-below versions qualify.
        var versions = new[] { "16.6", "16.5", "16.4" };

        var previous = ChampionAggregateScopeResolver.ResolvePreviousPatchVersion(versions, "16.5");

        previous.Should().Be("16.4");
    }

    [Fact]
    public void ResolvePreviousPatchVersion_returns_null_when_active_is_the_earliest()
    {
        var versions = new[] { "16.5" };

        var previous = ChampionAggregateScopeResolver.ResolvePreviousPatchVersion(versions, "16.5");

        previous.Should().BeNull("there is no patch below the only/earliest one");
    }

    [Fact]
    public void ResolvePreviousPatchVersion_returns_null_for_an_unparseable_active_patch()
    {
        var versions = new[] { "16.5", "16.4" };

        var previous = ChampionAggregateScopeResolver.ResolvePreviousPatchVersion(versions, "not-a-patch");

        previous.Should().BeNull();
    }

    [Fact]
    public void ResolvePreviousPatchVersion_skips_unparseable_candidate_versions()
    {
        var versions = new[] { "16.5", "garbage", "16.4" };

        var previous = ChampionAggregateScopeResolver.ResolvePreviousPatchVersion(versions, "16.5");

        previous.Should().Be("16.4", "malformed game versions are ignored rather than mis-ordered");
    }
}
