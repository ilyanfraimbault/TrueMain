using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using TrueMain.Services;

namespace TrueMain.Http;

/// <summary>
/// Answers a <see cref="ReadBackoffException"/> with <c>503 Service Unavailable</c> and a
/// <c>Retry-After</c> header (#2132 follow-up). The read is not broken in an unknown way — it
/// failed moments ago and is deliberately not re-run until its window ends — so a generic 500
/// told the caller less than the server knew. The body stays a ProblemDetails like every other
/// error, and names neither the read nor the failure behind it: the inner exception is logged
/// where it happened, not echoed.
/// </summary>
/// <remarks>
/// An MVC filter rather than an <c>IExceptionHandler</c>, so the mapping is the same in every
/// environment: Development renders unhandled exceptions with the developer page instead.
/// </remarks>
public sealed class ReadBackoffExceptionFilter(ProblemDetailsFactory problemDetailsFactory) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ReadBackoffException backoff)
        {
            return;
        }

        // Whole seconds, rounded up and at least one: Retry-After is an integer delay, and
        // "0" would invite the immediate retry the window exists to prevent.
        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(backoff.RetryAfter.TotalSeconds));
        context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        var problem = problemDetailsFactory.CreateProblemDetails(
            context.HttpContext,
            StatusCodes.Status503ServiceUnavailable,
            title: "Temporarily unavailable",
            detail: $"This read failed recently and is retried in {retryAfterSeconds} s.");

        context.Result = new ObjectResult(problem) { StatusCode = StatusCodes.Status503ServiceUnavailable };
        context.ExceptionHandled = true;
    }
}
