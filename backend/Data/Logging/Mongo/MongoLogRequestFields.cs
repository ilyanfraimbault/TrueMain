using Microsoft.Extensions.Logging;

namespace Data.Logging.Mongo;

/// <summary>
/// The request an HTTP-side log record belongs to (#1555): which call failed, with
/// what status, how slowly, and the trace identifier the client was handed. Read
/// from two places, because the two kinds of rows carry it differently.
/// </summary>
/// <remarks>
/// <para>
/// A row written by the API's own request logging (the rate-limit recorder, the
/// request-outcome middleware) names the fields in its message template, so they
/// arrive in the structured state under the keys below.
/// </para>
/// <para>
/// A row written by anything else during a request — the framework's
/// "unhandled exception" line, an EF Core command failure — only has the request
/// in its <em>scopes</em>: ASP.NET hosting opens one carrying <c>RequestId</c> and
/// <c>RequestPath</c> around every request. <c>RequestId</c> is
/// <c>HttpContext.TraceIdentifier</c>, the same value ProblemDetails returns as
/// <c>traceId</c>, so it is what lands in <see cref="TraceId"/>. The activity
/// scope's own <c>TraceId</c> key (the W3C trace id) is deliberately ignored: it
/// is a different value, and a client-visible id that matches no row is worse
/// than none.
/// </para>
/// <para>State wins over scopes: a template that names a field is more specific than the request around it.</para>
/// </remarks>
internal readonly record struct MongoLogRequestFields(
    string? TraceId,
    string? RequestMethod,
    string? RequestPath,
    int? StatusCode,
    long? DurationMs)
{
    public const string TraceIdKey = "TraceId";
    public const string RequestMethodKey = "RequestMethod";
    public const string RequestPathKey = "RequestPath";
    public const string StatusCodeKey = "StatusCode";
    public const string DurationMsKey = "DurationMs";

    // ASP.NET hosting's per-request scope key for HttpContext.TraceIdentifier.
    private const string HostingRequestIdKey = "RequestId";

    public static MongoLogRequestFields Extract<TState>(TState state, IExternalScopeProvider? scopes)
    {
        var fields = default(MongoLogRequestFields);

        scopes?.ForEachScope(
            (scope, _) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    fields = fields.Merge(pairs, fromScope: true);
                }
            },
            0);

        if (state is IEnumerable<KeyValuePair<string, object?>> statePairs)
        {
            fields = fields.Merge(statePairs, fromScope: false);
        }

        return fields;
    }

    private MongoLogRequestFields Merge(IEnumerable<KeyValuePair<string, object?>> pairs, bool fromScope)
    {
        var merged = this;
        foreach (var (key, value) in pairs)
        {
            merged = key switch
            {
                HostingRequestIdKey when fromScope => merged with { TraceId = AsString(value) ?? merged.TraceId },
                TraceIdKey when !fromScope => merged with { TraceId = AsString(value) ?? merged.TraceId },
                RequestMethodKey => merged with { RequestMethod = AsString(value) ?? merged.RequestMethod },
                RequestPathKey => merged with { RequestPath = AsString(value) ?? merged.RequestPath },
                StatusCodeKey when !fromScope => merged with { StatusCode = AsInt(value) ?? merged.StatusCode },
                DurationMsKey when !fromScope => merged with { DurationMs = AsLong(value) ?? merged.DurationMs },
                _ => merged
            };
        }

        return merged;
    }

    private static string? AsString(object? value)
        => value?.ToString() is { Length: > 0 } text ? text : null;

    private static int? AsInt(object? value) => value switch
    {
        int number => number,
        long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
        string text when int.TryParse(text, out var parsed) => parsed,
        _ => null
    };

    private static long? AsLong(object? value) => value switch
    {
        long number => number,
        int number => number,
        double number => (long)number,
        string text when long.TryParse(text, out var parsed) => parsed,
        _ => null
    };
}
