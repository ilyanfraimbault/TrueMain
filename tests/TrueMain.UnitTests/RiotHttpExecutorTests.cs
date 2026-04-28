using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Ingestor.Riot;
using Microsoft.Extensions.Logging.Abstractions;

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

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(20, 30)]
    public void GetExponentialBackoff_DoublesEachAttemptAndCapsAt30Seconds(int attempt, int expectedSeconds)
    {
        var backoff = RiotHttpExecutor.GetExponentialBackoff(attempt);

        backoff.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.NotImplemented, false)]
    public void IsTransientServerError_RecognisesRetryableStatuses(HttpStatusCode statusCode, bool expected)
    {
        RiotHttpExecutor.IsTransientServerError(statusCode).Should().Be(expected);
    }

    [Fact]
    public async Task GetAsync_WithSuccessOnFirstAttempt_ReturnsPayload()
    {
        using var handler = new ScriptedHandler(
            (_, _) => HttpResponse(HttpStatusCode.OK, "{\"value\":42}"));
        var executor = new RiotHttpExecutor(NullLogger<RiotHttpExecutor>.Instance);
        using var client = new HttpClient(handler);

        var result = await executor.GetAsync<Payload>(
            client,
            new Uri("https://riot.test/api"),
            maxRetryAttempts: 3,
            clientName: nameof(GetAsync_WithSuccessOnFirstAttempt_ReturnsPayload),
            ct: CancellationToken.None);

        result.Value.Should().Be(42);
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_RetriesTransientServerErrors_BeforeSucceeding()
    {
        using var handler = new ScriptedHandler(
            (call, _) => call switch
            {
                1 => HttpResponse(HttpStatusCode.ServiceUnavailable, ""),
                2 => HttpResponse(HttpStatusCode.BadGateway, ""),
                _ => HttpResponse(HttpStatusCode.OK, "{\"value\":7}")
            });
        var executor = new RiotHttpExecutor(NullLogger<RiotHttpExecutor>.Instance);
        using var client = new HttpClient(handler);

        var result = await executor.GetAsync<Payload>(
            client,
            new Uri("https://riot.test/api"),
            maxRetryAttempts: 5,
            clientName: nameof(GetAsync_RetriesTransientServerErrors_BeforeSucceeding),
            ct: CancellationToken.None);

        result.Value.Should().Be(7);
        handler.Calls.Should().Be(3);
    }

    [Fact]
    public async Task GetAsync_RetriesNetworkExceptions_BeforeSucceeding()
    {
        using var handler = new ScriptedHandler(
            (call, _) => call switch
            {
                1 => throw new HttpRequestException("connection reset"),
                _ => HttpResponse(HttpStatusCode.OK, "{\"value\":99}")
            });
        var executor = new RiotHttpExecutor(NullLogger<RiotHttpExecutor>.Instance);
        using var client = new HttpClient(handler);

        var result = await executor.GetAsync<Payload>(
            client,
            new Uri("https://riot.test/api"),
            maxRetryAttempts: 3,
            clientName: nameof(GetAsync_RetriesNetworkExceptions_BeforeSucceeding),
            ct: CancellationToken.None);

        result.Value.Should().Be(99);
        handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_PropagatesNon429ClientErrorsImmediately()
    {
        using var handler = new ScriptedHandler(
            (_, _) => HttpResponse(HttpStatusCode.NotFound, ""));
        var executor = new RiotHttpExecutor(NullLogger<RiotHttpExecutor>.Instance);
        using var client = new HttpClient(handler);

        var act = async () => await executor.GetAsync<Payload>(
            client,
            new Uri("https://riot.test/api"),
            maxRetryAttempts: 3,
            clientName: nameof(GetAsync_PropagatesNon429ClientErrorsImmediately),
            ct: CancellationToken.None);

        var exception = await act.Should().ThrowAsync<HttpRequestException>();
        exception.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
        handler.Calls.Should().Be(1);
    }

    private static HttpResponseMessage HttpResponse(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body) };

    private sealed class Payload
    {
        public int Value { get; init; }
    }

    private sealed class ScriptedHandler(Func<int, HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(Calls, request));
        }
    }
}
