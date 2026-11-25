using System.Diagnostics.CodeAnalysis;

namespace TrueMain.Services.Ops;

/// <summary>
/// A measurement that may be unavailable: its value, or why there is none. An unavailable
/// measurement is an answer the ops panels show ("unknown, because …"), not an error that blanks
/// the page — and never a zero that reads like one.
/// </summary>
public readonly record struct Measured<T>(T? Value, string? UnknownReason)
{
    [MemberNotNullWhen(false, nameof(UnknownReason))]
    public bool IsMeasured => UnknownReason is null;
}

/// <summary>
/// Runs the ops panels' measurements: a failed one is logged once and becomes a
/// <see cref="Measured{T}"/> carrying its reason, so one broken query costs its own tile, never
/// the payload around it.
/// </summary>
/// <remarks>
/// The reason never echoes raw exception text in production — it can carry filesystem paths,
/// connection-string fragments or internal type names; operators reach for the log line. Outside
/// production it is the exception's message, so a broken query is diagnosable from the page.
/// </remarks>
/// <typeparam name="TCategory">The reading service, the category its failures are logged under.</typeparam>
public sealed class Measurements<TCategory>(IHostEnvironment environment, ILogger<TCategory> logger)
{
    public async Task<Measured<T>> TryAsync<T>(string what, Func<Task<T>> measure, CancellationToken ct)
    {
        try
        {
            return new Measured<T>(await measure(), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "{Measurement} failed to measure", what);
            return new Measured<T>(default, ReasonFor(ex));
        }
        finally
        {
            ct.ThrowIfCancellationRequested();
        }
    }

    /// <summary>What a failed measurement says about its failure.</summary>
    public string ReasonFor(Exception exception)
        => environment.IsProduction() || string.IsNullOrWhiteSpace(exception.Message)
            ? Measurements.InternalError
            : exception.Message;
}

/// <summary>What every unavailable measurement says in production.</summary>
public static class Measurements
{
    /// <summary>The reason shown in production, whatever failed.</summary>
    public const string InternalError = "internal error";
}
