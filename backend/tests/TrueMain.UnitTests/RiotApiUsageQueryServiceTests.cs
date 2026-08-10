using AwesomeAssertions;
using Data.Metrics.Mongo;
using TrueMain.Services.Ops;

namespace TrueMain.UnitTests;

/// <summary>
/// The budget-headroom arithmetic (#1035): binding-limit selection across the app
/// rate-limit's windows, the three "not enough to trust an extrapolation" guards,
/// and the <c>X-App-Rate-Limit</c> pair parser. Exercises the internal statics
/// directly (<c>InternalsVisibleTo</c>) — no Mongo/Postgres involved, since this is
/// pure arithmetic over already-fetched inputs.
/// </summary>
public sealed class RiotApiUsageQueryServiceTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void BuildHeadroom_ObservedSpanBelowTwentyFourHours_IsInsufficientData()
    {
        var saturation = new RiotApiSaturationInputs(
            TotalCalls: 10_000,
            EarliestBucketUtc: Now.AddHours(-2),
            RateLimit: RateLimit("20:1,100:120", "5:1,80:120"));

        var headroom = RiotApiUsageQueryService.BuildHeadroom(saturation, trackedAccounts: 100);

        headroom.SufficientData.Should().BeFalse();
        headroom.ObservedWindowHours.Should().BeApproximately(2, 0.1);
        headroom.RequiredWindowHours.Should().Be(24);
        headroom.AdditionalAccountsHeadroom.Should().BeNull();
    }

    [Fact]
    public void BuildHeadroom_NoTrackedAccounts_IsInsufficientData()
    {
        var saturation = new RiotApiSaturationInputs(
            TotalCalls: 10_000,
            EarliestBucketUtc: Now.AddDays(-7),
            RateLimit: RateLimit("20:1,100:120", "5:1,80:120"));

        var headroom = RiotApiUsageQueryService.BuildHeadroom(saturation, trackedAccounts: 0);

        headroom.SufficientData.Should().BeFalse();
        headroom.TrackedAccounts.Should().Be(0);
    }

    [Fact]
    public void BuildHeadroom_NoRateLimitSnapshot_IsInsufficientData()
    {
        var saturation = new RiotApiSaturationInputs(
            TotalCalls: 10_000,
            EarliestBucketUtc: Now.AddDays(-7),
            RateLimit: null);

        var headroom = RiotApiUsageQueryService.BuildHeadroom(saturation, trackedAccounts: 100);

        headroom.SufficientData.Should().BeFalse();
    }

    [Fact]
    public void BuildHeadroom_SufficientData_ComputesCostPerAccountAndAdditionalHeadroom()
    {
        // 7 days exactly, 100 tracked accounts, 700,000 calls total -> 100,000/day
        // observed, 1,000 calls/account/day. Binding limit is the 120s window
        // (100 * 86400 / 120 = 72,000/day) rather than the 1s window
        // (20 * 86400 / 1 = 1,728,000/day) — the tighter sustained ceiling.
        var saturation = new RiotApiSaturationInputs(
            TotalCalls: 700_000,
            EarliestBucketUtc: Now.AddDays(-7),
            RateLimit: RateLimit("20:1,100:120", "5:1,80:120"));

        var headroom = RiotApiUsageQueryService.BuildHeadroom(saturation, trackedAccounts: 100);

        headroom.SufficientData.Should().BeTrue();
        headroom.TrackedAccounts.Should().Be(100);
        headroom.ObservedCallsPerDay!.Value.Should().BeApproximately(100_000, 50);
        headroom.CallsPerAccountPerDay!.Value.Should().BeApproximately(1_000, 1);
        headroom.BindingLimit.Should().NotBeNull();
        headroom.BindingLimit!.Limit.Should().Be(100);
        headroom.BindingLimit.WindowSeconds.Should().Be(120);
        headroom.BindingLimit.MaxCallsPerDay.Should().BeApproximately(72_000, 0.5);
        // Observed (100,000/day) already exceeds the binding ceiling (72,000/day):
        // spare is clamped to 0, not negative, and headroom is 0 more accounts.
        headroom.SpareCallsPerDay.Should().Be(0);
        headroom.AdditionalAccountsHeadroom.Should().Be(0);
    }

    [Fact]
    public void BuildHeadroom_SpareCapacity_YieldsPositiveAdditionalAccounts()
    {
        // 100,000 calls over 7 days -> ~14,286/day observed, ~143/account/day.
        // Binding ceiling (100:120) is 72,000/day, so ~57,714/day spare ->
        // floor(57714 / 142.857) = 404 additional accounts.
        var saturation = new RiotApiSaturationInputs(
            TotalCalls: 100_000,
            EarliestBucketUtc: Now.AddDays(-7),
            RateLimit: RateLimit("20:1,100:120", "5:1,80:120"));

        var headroom = RiotApiUsageQueryService.BuildHeadroom(saturation, trackedAccounts: 100);

        headroom.SufficientData.Should().BeTrue();
        headroom.SpareCallsPerDay!.Value.Should().BeGreaterThan(0);
        headroom.AdditionalAccountsHeadroom.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ResolveBindingLimit_MultiplePairs_PicksSmallestDailyCeiling()
    {
        // 1s window -> 1,728,000/day; 120s window -> 72,000/day (binding).
        var binding = RiotApiUsageQueryService.ResolveBindingLimit("20:1,100:120");

        binding.Should().NotBeNull();
        binding!.Limit.Should().Be(100);
        binding.WindowSeconds.Should().Be(120);
    }

    [Fact]
    public void ResolveBindingLimit_NullOrBlank_ReturnsNull()
    {
        RiotApiUsageQueryService.ResolveBindingLimit(null).Should().BeNull();
        RiotApiUsageQueryService.ResolveBindingLimit("   ").Should().BeNull();
    }

    [Theory]
    [InlineData("not-a-pair")]
    [InlineData("20:")]
    [InlineData("20:0")]
    [InlineData("abc:120")]
    public void ResolveBindingLimit_MalformedPairs_AreSkipped(string malformed)
    {
        RiotApiUsageQueryService.ResolveBindingLimit(malformed).Should().BeNull();
    }

    [Fact]
    public void ResolveBindingLimit_MixOfValidAndMalformedPairs_UsesOnlyTheValidOnes()
    {
        var binding = RiotApiUsageQueryService.ResolveBindingLimit("20:1,bad:pair,100:120");

        binding.Should().NotBeNull();
        binding!.WindowSeconds.Should().Be(120);
    }

    private static RiotApiRateLimitSnapshot RateLimit(string appLimit, string appCount)
        => new(Now, appLimit, appCount, null, null, null, null);
}
