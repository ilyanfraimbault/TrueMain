using AwesomeAssertions;
using Data.Entities;
using Data.Ops.Mongo;
using NSubstitute;
using TrueMain.Services.Ops;

namespace TrueMain.UnitTests;

/// <summary>
/// The paged seed-request list (#1166). What is worth pinning here is the
/// translation from a 1-based page to a skip/take, and the clamps around it: the
/// queue is fed in bulk by the weekly OTP seeder, so this list is now the only way
/// to see past its newest rows and an off-by-one page would silently hide or repeat
/// a whole page of it.
/// </summary>
public sealed class SeedRequestQueryServiceTests
{
    [Theory]
    // page, pageSize -> skip, take
    [InlineData(null, null, 0, 25)]      // defaults
    [InlineData(1, 25, 0, 25)]           // first page starts at zero, not at pageSize
    [InlineData(3, 25, 50, 25)]
    [InlineData(0, 25, 0, 25)]           // page 0 and negatives clamp up to page 1
    [InlineData(-4, 25, 0, 25)]
    [InlineData(2, 0, 1, 1)]             // pageSize clamps up to 1
    [InlineData(2, 5000, 100, 100)]      // and down to 100
    public async Task GetPageAsync_TranslatesPageToSkipAndTake(
        int? page, int? pageSize, int expectedSkip, int expectedTake)
    {
        var store = EmptyStore();

        await CreateService(store).GetPageAsync(
            status: null, search: null, region: null, page, pageSize, CancellationToken.None);

        await store.Received(1).GetPageAsync(
            Arg.Any<SeedRequestStatus?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            expectedSkip,
            expectedTake,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPageAsync_EchoesTheClampedPageBackSoThePagerAgreesWithTheRows()
    {
        // The pager renders from the response, not from what it asked for. Echoing the
        // requested page instead of the clamped one would leave it highlighting page
        // 5000 while showing page 100's rows.
        var store = EmptyStore();

        var result = await CreateService(store).GetPageAsync(
            status: null, search: null, region: null, page: -4, pageSize: 5000, CancellationToken.None);

        result.Page.Should().Be(1);
        result.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task GetPageAsync_PassesTheFiltersThroughAndReturnsTheUnpagedTotal()
    {
        var store = Substitute.For<ISeedRequestStore>();
        store.GetPageAsync(
                Arg.Any<SeedRequestStatus?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SeedRequestPage([Document("Faker", "KR1", "KR")], Total: 11_565));

        var result = await CreateService(store).GetPageAsync(
            status: "pending", search: "fak", region: "KR", page: 1, pageSize: 25,
            CancellationToken.None);

        // Total is the count across all pages, not the page's own length — that is the
        // number the panel reports as "how much is queued".
        result.Total.Should().Be(11_565);
        result.Requests.Should().ContainSingle().Which.GameName.Should().Be("Faker");

        await store.Received(1).GetPageAsync(
            SeedRequestStatus.Pending, "fak", "KR", 0, 25, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPageAsync_TreatsAnUnknownStatusAsNoFilterRatherThanNoResults()
    {
        // Same leniency the unpaged read has always had: a status the enum does not know
        // is dropped, so a stale bookmark widens the list instead of emptying it.
        var store = EmptyStore();

        await CreateService(store).GetPageAsync(
            status: "not-a-status", search: null, region: null, page: 1, pageSize: 25,
            CancellationToken.None);

        await store.Received(1).GetPageAsync(
            null, Arg.Any<string?>(), Arg.Any<string?>(),
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private static ISeedRequestStore EmptyStore()
    {
        var store = Substitute.For<ISeedRequestStore>();
        store.GetPageAsync(
                Arg.Any<SeedRequestStatus?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new SeedRequestPage([], Total: 0));
        return store;
    }

    private static SeedRequestQueryService CreateService(ISeedRequestStore store) => new(store);

    private static SeedRequestDocument Document(string gameName, string tagLine, string platformId)
        => new()
        {
            Id = Guid.NewGuid(),
            GameName = gameName,
            TagLine = tagLine,
            PlatformId = platformId,
            Status = SeedRequestStatus.Pending,
            RequestedAtUtc = new DateTime(2026, 8, 20, 2, 26, 0, DateTimeKind.Utc)
        };
}
