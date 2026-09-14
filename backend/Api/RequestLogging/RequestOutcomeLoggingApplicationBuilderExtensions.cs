namespace TrueMain.RequestLogging;

public static class RequestOutcomeLoggingApplicationBuilderExtensions
{
    /// <summary>
    /// Adds <see cref="RequestOutcomeLoggingMiddleware"/>. Call it before the
    /// exception handler, so the 500 that handler writes is seen.
    /// </summary>
    public static IApplicationBuilder UseRequestOutcomeLogging(this IApplicationBuilder app)
        => app.UseMiddleware<RequestOutcomeLoggingMiddleware>();
}
