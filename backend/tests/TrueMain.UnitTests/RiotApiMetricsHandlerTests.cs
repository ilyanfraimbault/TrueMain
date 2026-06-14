using System.Net;
using AwesomeAssertions;
using Data.Metrics.Mongo;
using Ingestor.Riot;
using NSubstitute;

namespace TrueMain.UnitTests;

public sealed class RiotApiMetricsHandlerTests
{
    [Fact]
    public async Task SendAsync_SuccessfulResponse_RecordsEndpointStatusAndRateLimitHeaders()
    {
        var recorder = Substitute.For<IRiotApiCallRecorder>();
        RiotApiCallRecord? captured = null;
        recorder.Record(Arg.Do<RiotApiCallRecord>(record => captured = record));

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("X-App-Rate-Limit", "20:1,100:120");
        response.Headers.TryAddWithoutValidation("X-App-Rate-Limit-Count", "3:1,57:120");

        // `handler` owns the inner stub; the invoker is told not to dispose the
        // handler (disposeHandler: false) so each disposable is released exactly
        // once by its own `using`.
        using var handler = new RiotApiMetricsHandler(recorder) { InnerHandler = new StubHandler(response) };
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "https://europe.api.riotgames.com/lol/match/v5/matches/EUW1_1");

        using var result = await invoker.SendAsync(request, CancellationToken.None);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().NotBeNull();
        captured!.Endpoint.Should().Be("match-v5.match");
        captured.Route.Should().Be("europe");
        captured.Method.Should().Be("GET");
        captured.StatusCode.Should().Be(200);
        captured.AppRateLimit.Should().Be("20:1,100:120");
        captured.AppRateLimitCount.Should().Be("3:1,57:120");
    }

    [Fact]
    public async Task SendAsync_With429_CapturesRetryAfterSeconds()
    {
        var recorder = Substitute.For<IRiotApiCallRecorder>();
        RiotApiCallRecord? captured = null;
        recorder.Record(Arg.Do<RiotApiCallRecord>(record => captured = record));

        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("Retry-After", "5");
        response.Headers.TryAddWithoutValidation("X-Rate-Limit-Type", "application");

        using var handler = new RiotApiMetricsHandler(recorder) { InnerHandler = new StubHandler(response) };
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "https://europe.api.riotgames.com/lol/match/v5/matches/EUW1_1/timeline");

        using var result = await invoker.SendAsync(request, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.StatusCode.Should().Be(429);
        captured.RetryAfterSeconds.Should().Be(5);
        captured.RateLimitType.Should().Be("application");
    }

    [Fact]
    public async Task SendAsync_WhenInnerThrows_RecordsStatusZeroAndRethrows()
    {
        var recorder = Substitute.For<IRiotApiCallRecorder>();
        RiotApiCallRecord? captured = null;
        recorder.Record(Arg.Do<RiotApiCallRecord>(record => captured = record));

        using var handler = new RiotApiMetricsHandler(recorder)
        {
            InnerHandler = new StubHandler(new HttpRequestException("boom"))
        };
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "https://euw1.api.riotgames.com/lol/summoner/v4/summoners/by-puuid/PUUID");

        var act = async () => await invoker.SendAsync(request, CancellationToken.None);

        // The fault must propagate to the caller unchanged...
        await act.Should().ThrowAsync<HttpRequestException>();
        // ...while still being recorded as a real attempt with status 0 (no response).
        captured.Should().NotBeNull();
        captured!.Endpoint.Should().Be("summoner-v4.byPuuid");
        captured.StatusCode.Should().Be(0);
    }

    /// <summary>Inner handler that returns a fixed response or faults the send.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? _response;
        private readonly Exception? _exception;

        public StubHandler(HttpResponseMessage response) => _response = response;

        public StubHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => _exception is not null
                ? Task.FromException<HttpResponseMessage>(_exception)
                : Task.FromResult(_response!);
    }
}
