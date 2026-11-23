using System.Diagnostics.CodeAnalysis;

namespace Data.Statics;

/// <summary>How a static-data read was answered.</summary>
public enum StaticDataStatus
{
    /// <summary>The asked patch's own data.</summary>
    Available,

    /// <summary>
    /// The source has not published the asked patch yet (patch day): the newest published data
    /// answers instead. Item ids, ranges and classes rarely move between two patches, so callers
    /// use it as they would the real thing.
    /// </summary>
    Fallback,

    /// <summary>The source could not be read. Already logged where it failed.</summary>
    Unavailable,
}

/// <summary>
/// A static-data answer: the data and how it was obtained, or why there is none. Static game
/// data is an external dependency that can be down for a patch; the providers say so with this
/// value rather than by throwing, so each caller spells its degraded answer as a branch.
/// </summary>
public sealed record StaticData<T>
    where T : class
{
    private StaticData(StaticDataStatus status, T? value, string? reason)
    {
        Status = status;
        Value = value;
        Reason = reason;
    }

    public StaticDataStatus Status { get; }

    /// <summary>The data, present unless <see cref="Status"/> is <see cref="StaticDataStatus.Unavailable"/>.</summary>
    public T? Value { get; }

    /// <summary>Why the data is unavailable; null otherwise.</summary>
    public string? Reason { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    public bool HasValue => Value is not null;

    public static StaticData<T> Available(T value) => new(StaticDataStatus.Available, value, null);

    public static StaticData<T> Fallback(T value) => new(StaticDataStatus.Fallback, value, null);

    public static StaticData<T> Unavailable(string reason) => new(StaticDataStatus.Unavailable, null, reason);

    /// <summary>
    /// The data, for a caller whose answer would be wrong without it (a fold that must not
    /// persist item-less facts): an unavailable source becomes an exception that fails the
    /// caller's batch, to be retried on its next pass.
    /// </summary>
    public T Require(string what)
        => Value ?? throw new StaticDataUnavailableException($"{what} unavailable: {Reason}");
}

/// <summary>Thrown by <see cref="StaticData{T}.Require"/> when a caller cannot go on without the data.</summary>
public sealed class StaticDataUnavailableException(string message) : Exception(message);
