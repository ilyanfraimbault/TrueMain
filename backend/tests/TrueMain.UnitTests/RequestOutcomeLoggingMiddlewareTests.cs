using Data.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TrueMain.RequestLogging;
using TrueMain.TestKit;

namespace TrueMain.UnitTests;

/// <summary>
/// The requests that went wrong must be findable in the ops logs by path, status
/// and the traceId the client was given; the ones that did not must stay out (#1555).
/// </summary>
public class RequestOutcomeLoggingMiddlewareTests
{
    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/champions/103";
        context.Request.QueryString = new QueryString("?patch=16.18");
        context.TraceIdentifier = "0HN7:00000001";
        return context;
    }

    private static object? Property(CapturedLog entry, string key)
        => entry.Properties.Single(property => property.Key == key).Value;

    [Fact]
    public async Task LogsA5xxAnswerWithItsRequest()
    {
        var logger = new CapturingLogger<RequestOutcomeLoggingMiddleware>();
        var middleware = new RequestOutcomeLoggingMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(Context());

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(OpsEvents.RequestFailed, entry.EventId);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("GET", Property(entry, "RequestMethod"));
        Assert.Equal("/champions/103?patch=16.18", Property(entry, "RequestPath"));
        Assert.Equal(503, Property(entry, "StatusCode"));
        Assert.Equal("0HN7:00000001", Property(entry, "TraceId"));
        Assert.IsType<long>(Property(entry, "DurationMs"));
    }

    [Fact]
    public async Task LogsAnEscapedExceptionAsA500AndRethrowsIt()
    {
        var logger = new CapturingLogger<RequestOutcomeLoggingMiddleware>();
        var middleware = new RequestOutcomeLoggingMiddleware(
            _ => throw new InvalidOperationException("boom"),
            logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(Context()));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(OpsEvents.RequestFailed, entry.EventId);
        Assert.Equal(500, Property(entry, "StatusCode"));
    }

    [Fact]
    public async Task LogsARequestTheClientAbandoned()
    {
        var logger = new CapturingLogger<RequestOutcomeLoggingMiddleware>();
        var middleware = new RequestOutcomeLoggingMiddleware(_ => Task.CompletedTask, logger);
        var context = Context();
        context.RequestAborted = new CancellationToken(canceled: true);

        await middleware.InvokeAsync(context);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(OpsEvents.RequestAborted, entry.EventId);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("0HN7:00000001", Property(entry, "TraceId"));
    }

    [Theory]
    [InlineData(StatusCodes.Status200OK)]
    [InlineData(StatusCodes.Status404NotFound)]
    [InlineData(StatusCodes.Status429TooManyRequests)]
    public async Task StaysSilentBelow500(int statusCode)
    {
        var logger = new CapturingLogger<RequestOutcomeLoggingMiddleware>();
        var middleware = new RequestOutcomeLoggingMiddleware(
            context =>
            {
                context.Response.StatusCode = statusCode;
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(Context());

        Assert.Empty(logger.Entries);
    }
}
