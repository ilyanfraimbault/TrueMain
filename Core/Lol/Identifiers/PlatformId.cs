namespace Core.Lol.Identifiers;

/// <summary>
/// Value object wrapping a Riot platform identifier (e.g. "EUW1", "NA1").
/// Validates that the underlying string matches a known <see cref="PlatformRoute"/>.
/// <para>
/// <c>default(PlatformId)</c> is intentionally invalid: <see cref="Route"/>,
/// <see cref="Value"/> and the implicit conversions throw rather than silently
/// returning <see cref="PlatformRoute.BR1"/> (the underlying enum's zero value).
/// </para>
/// </summary>
public readonly record struct PlatformId
{
    private readonly PlatformRoute _route;
    private readonly bool _initialized;

    public PlatformId(PlatformRoute route)
    {
        if (!Enum.IsDefined(route))
        {
            throw new ArgumentException($"Unknown platform route: {route}.", nameof(route));
        }

        _route = route;
        _initialized = true;
    }

    public PlatformRoute Route => _initialized
        ? _route
        : throw new InvalidOperationException("Default PlatformId is not a valid platform.");

    public string Value => Route.ToString();

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
