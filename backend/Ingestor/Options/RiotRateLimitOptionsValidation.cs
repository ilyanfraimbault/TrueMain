using Ingestor.Riot.RateLimiting;

namespace Ingestor.Options;

/// <summary>
/// Startup validation helper for <see cref="RiotRateLimitOptions"/>.
/// </summary>
/// <remarks>
/// The parsing itself lives with the limiter (<see cref="RiotRateLimitWindow.ParseHeader"/>)
/// because Riot's limit headers and this configured fallback share one grammar — validating
/// with a second, local parser is how the two would drift apart and let a boot-valid value
/// be silently ignored at runtime.
/// </remarks>
internal static class RiotRateLimitOptionsValidation
{
    /// <summary>
    /// Whether the configured value declares at least one usable window. An empty or
    /// entirely malformed value would leave the application budget with no windows, which
    /// enforces nothing — the failure mode the limiter exists to prevent — so it fails
    /// the boot instead.
    /// </summary>
    public static bool HasParsableWindow(string? appLimits)
        => RiotRateLimitWindow.ParseHeader(appLimits).Any();

    /// <summary>
    /// Whether the reserved shares leave most of a regional route to everyone else: each
    /// between 0 and 0.5, and at most half of the window in total. Reservations that summed
    /// to the whole window would let the reserved callers hold the route against the
    /// pipeline's main consumer for as long as they stay active.
    /// </summary>
    public static bool HasBoundedReservations(IReadOnlyDictionary<string, double>? shares)
        => shares is null
           || (shares.Values.All(share => share is >= 0 and <= 0.5) && shares.Values.Sum() <= 0.5);
}
