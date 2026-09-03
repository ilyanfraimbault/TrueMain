using AwesomeAssertions;
using TrueMain.Services.Ops;

namespace TrueMain.UnitTests;

/// <summary>
/// <see cref="ChampionStatsQueryService"/> caches its answer per
/// (region, patch, position, queue) so a hot operator page — the Overview
/// top-10 chart calls it unfiltered on every load (#1412) — does not re-scan
/// match_participants ⋈ matches on every request. The one way that goes wrong
/// is a key collision: if a filtered call ever resolved to the same cache
/// entry as the unfiltered one (or another filter combination), operators
/// would silently see the wrong slice's numbers. These tests pin the key
/// construction directly rather than exercising <c>GetAsync</c>, which needs a
/// real Postgres connection for its raw SQL (see the integration test).
/// </summary>
public sealed class ChampionStatsQueryServiceCacheKeyTests
{
    [Fact]
    public void BuildCacheKey_FilteredCall_DiffersFromUnfilteredKey()
    {
        var unfilteredKey = ChampionStatsQueryService.BuildCacheKey(
            region: null, patch: null, position: null, queue: null);

        var filteredKey = ChampionStatsQueryService.BuildCacheKey(
            region: "EUW1", patch: "16.4", position: "MIDDLE", queue: 420);

        filteredKey.Should().NotBe(unfilteredKey);
    }

    [Fact]
    public void BuildCacheKey_EachFilterAlone_ProducesADistinctKeyFromUnfiltered()
    {
        var unfilteredKey = ChampionStatsQueryService.BuildCacheKey(null, null, null, null);

        var regionOnly = ChampionStatsQueryService.BuildCacheKey("EUW1", null, null, null);
        var patchOnly = ChampionStatsQueryService.BuildCacheKey(null, "16.4", null, null);
        var positionOnly = ChampionStatsQueryService.BuildCacheKey(null, null, "MIDDLE", null);
        var queueOnly = ChampionStatsQueryService.BuildCacheKey(null, null, null, 420);

        var keys = new[] { unfilteredKey, regionOnly, patchOnly, positionOnly, queueOnly };
        keys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void BuildCacheKey_DifferentQueueValues_ProduceDistinctKeys()
    {
        // Queue is the one int filter: guard against a naive key that folds a
        // set-but-different value into the same string as another (or as null).
        var queue420 = ChampionStatsQueryService.BuildCacheKey(null, null, null, 420);
        var queue440 = ChampionStatsQueryService.BuildCacheKey(null, null, null, 440);
        var queueNull = ChampionStatsQueryService.BuildCacheKey(null, null, null, null);

        var keys = new[] { queue420, queue440, queueNull };
        keys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void BuildCacheKey_SameFilterTuple_ProducesTheSameKey()
    {
        // Cache lookups only work if two calls with an identical filter tuple
        // agree on the key byte-for-byte.
        var first = ChampionStatsQueryService.BuildCacheKey("EUW1", "16.4", "MIDDLE", 420);
        var second = ChampionStatsQueryService.BuildCacheKey("EUW1", "16.4", "MIDDLE", 420);

        second.Should().Be(first);
    }

    [Fact]
    public void BuildCacheKey_DoesNotLetTheDelimiterBeForgedFromInsideAFilterValue()
    {
        // The one way this goes wrong: an unescaped ':' join lets a value that itself
        // contains ':' shift the field boundary. Region "A:B" + patch "C" and region "A"
        // + patch "B:C" would render identically as "...A:B:C..." if the segments were
        // not escaped first.
        var shiftedIntoRegion = ChampionStatsQueryService.BuildCacheKey("A:B", "C", "D", null);
        var shiftedIntoPatch = ChampionStatsQueryService.BuildCacheKey("A", "B:C", "D", null);

        shiftedIntoRegion.Should().NotBe(shiftedIntoPatch);
    }

    [Fact]
    public void BuildCacheKey_EscapesABackslashSoItCannotUnescapeTheNextDelimiter()
    {
        // A value ending in a literal backslash must not be able to "consume" the escape
        // meant for the delimiter that follows it.
        var literalBackslash = ChampionStatsQueryService.BuildCacheKey("A\\", "B", "C", null);
        var escapedColon = ChampionStatsQueryService.BuildCacheKey("A", "B", "C", null);

        literalBackslash.Should().NotBe(escapedColon);
    }
}
