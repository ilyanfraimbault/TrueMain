using System.Collections.Frozen;

namespace Core.Lol.Identifiers;

/// <summary>
/// Value object wrapping a Riot platform identifier (e.g. "EUW1", "NA1").
/// Validates that the underlying string matches a known <see cref="PlatformRoute"/>.
/// <para>
/// <c>default(PlatformId)</c> is intentionally invalid: <see cref="Route"/>,
/// <see cref="Value"/> and the implicit conversions throw. <see cref="PlatformRoute"/>
/// leaves 0 unused, so a default route is never a real platform.
/// </para>
/// </summary>
public readonly record struct PlatformId
{
    // Per-route display strings, precomputed once so Value / ToString never
    // allocate a fresh string via Enum.ToString() on the hot path (~16 routes).
    private static readonly FrozenDictionary<PlatformRoute, string> RouteNames =
        Enum.GetValues<PlatformRoute>().ToFrozenDictionary(route => route, route => route.ToString());

    public PlatformId(PlatformRoute route)
    {
        if (!Enum.IsDefined(route))
        {
            throw new ArgumentException($"Unknown platform route: {route}.", nameof(route));
        }

        Route = route;
    }

    // PlatformRoute leaves 0 unused, so default(PlatformId) — a 0 route — reads back as
    // uninitialized with no offset or flag beside it.
    public PlatformRoute Route
    {
        get => field != default
            ? field
            : throw new InvalidOperationException("Default PlatformId is not a valid platform.");
        private init;
    }

    public string Value => RouteNames[Route];

    public static PlatformId Parse(string value)
    {
        if (!TryParse(value, out var platformId))
        {
            throw new ArgumentException($"Unknown platform id: '{value}'.", nameof(value));
        }

        return platformId;
    }

    public static bool TryParse(string? value, out PlatformId platformId)
    {
        platformId = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        // Enum.TryParse accepts numeric strings ("0" -> BR1); reject them so only named routes are valid.
        if (LooksNumeric(trimmed))
        {
            return false;
        }

        if (!Enum.TryParse<PlatformRoute>(trimmed, ignoreCase: true, out var route)
            || !Enum.IsDefined(route))
        {
            return false;
        }

        platformId = new PlatformId(route);
        return true;
    }

    public override string ToString() => Value;

    private static bool LooksNumeric(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsDigit(c) && c != '+' && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    public static implicit operator string(PlatformId platformId) => platformId.Value;
    public static implicit operator PlatformRoute(PlatformId platformId) => platformId.Route;
    public static implicit operator PlatformId(PlatformRoute route) => new(route);
    public static explicit operator PlatformId(string value) => Parse(value);
}
