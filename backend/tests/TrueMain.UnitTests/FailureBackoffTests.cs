using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.Services;

namespace TrueMain.UnitTests;

/// <summary>
/// The negative cache in front of the expensive ops reads (#2132). What it has to get right:
/// a failure is not re-run within its window, the marker actually lands in the size-limited
/// shared cache (a sizeless entry is silently dropped there, which would look exactly like no
/// backoff at all), and a cancelled call opens no window.
/// </summary>
public sealed class FailureBackoffTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task RunAsync_DoesNotRerunAFailure_WithinTheWindow()
    {
        using var cache = SizeLimitedCache();
        var calls = 0;
        var timeout = new InvalidOperationException("57014: canceling statement due to statement timeout");

        Task<int> Failing()
        {
            calls++;
            throw timeout;
        }

        await FluentActions.Awaiting(() => FailureBackoff.RunAsync(cache, "read", Window, Failing))
            .Should().ThrowAsync<InvalidOperationException>();

        var backingOff = await FluentActions.Awaiting(() => FailureBackoff.RunAsync(cache, "read", Window, Failing))
            .Should().ThrowAsync<ReadBackoffException>();

        backingOff.Which.InnerException.Should().BeSameAs(timeout);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_KeepsReadsIndependent_PerKey()
    {
        using var cache = SizeLimitedCache();

        await FluentActions.Awaiting(() => FailureBackoff.RunAsync<int>(
                cache, "filtered", Window, () => throw new InvalidOperationException()))
            .Should().ThrowAsync<InvalidOperationException>();

        (await FailureBackoff.RunAsync(cache, "unfiltered", Window, () => Task.FromResult(42)))
            .Should().Be(42);
    }

    [Fact]
    public async Task RunAsync_OpensNoWindow_WhenTheCallIsCancelled()
    {
        using var cache = SizeLimitedCache();

        await FluentActions.Awaiting(() => FailureBackoff.RunAsync<int>(
                cache, "read", Window, () => throw new OperationCanceledException()))
            .Should().ThrowAsync<OperationCanceledException>();

        (await FailureBackoff.RunAsync(cache, "read", Window, () => Task.FromResult(7)))
            .Should().Be(7);
    }

    private static MemoryCache SizeLimitedCache() => new(new MemoryCacheOptions { SizeLimit = 1024 });
}
