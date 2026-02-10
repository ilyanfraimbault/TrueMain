using System.Net.Http.Headers;
using FluentAssertions;
using Ingestor.Riot;

namespace TrueMain.UnitTests;

public sealed class RiotHttpExecutorTests
{
    [Fact]
    public void GetRetryDelay_WithDelta_ReturnsDelta()
    {
        var retryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));

        var delay = RiotHttpExecutor.GetRetryDelay(retryAfter, DateTimeOffset.UtcNow);

        delay.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GetRetryDelay_WithFutureDate_ReturnsPositiveDelay()
    {
        var now = DateTimeOffset.UtcNow;
        var retryAfter = new RetryConditionHeaderValue(now.AddSeconds(3));

        var delay = RiotHttpExecutor.GetRetryDelay(retryAfter, now);

        delay.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void GetRetryDelay_WithPastDate_ReturnsOneSecond()
    {
        var now = DateTimeOffset.UtcNow;
        var retryAfter = new RetryConditionHeaderValue(now.AddSeconds(-2));

        var delay = RiotHttpExecutor.GetRetryDelay(retryAfter, now);

        delay.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GetRetryDelay_WithNoHeader_ReturnsOneSecond()
    {
        var delay = RiotHttpExecutor.GetRetryDelay(null, DateTimeOffset.UtcNow);

        delay.Should().Be(TimeSpan.FromSeconds(1));
    }
}
