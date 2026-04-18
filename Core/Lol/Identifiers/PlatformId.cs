namespace Core.Lol.Identifiers;

/// <summary>
/// Value object wrapping a Riot platform identifier (e.g. "EUW1", "NA1").
/// Validates that the underlying string matches a known <see cref="PlatformRoute"/>.
/// </summary>
public readonly record struct PlatformId
{
    private readonly PlatformRoute _route;

    public PlatformId(PlatformRoute route)
    {
        if (!Enum.IsDefined(route))
        {
            throw new ArgumentException($"Unknown platform route: {route}.", nameof(route));
        }

        _route = route;
    }

    public PlatformRoute Route => _route;

    public string Value => _route.ToString();

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
        if (string.IsNullOrWhiteSpace(value)
            || !Enum.TryParse<PlatformRoute>(value, ignoreCase: false, out var route)
            || !Enum.IsDefined(route))
        {
            platformId = default;
            return false;
        }

        platformId = new PlatformId(route);
        return true;
    }

    public override string ToString() => Value;

    public static implicit operator string(PlatformId platformId) => platformId.Value;
    public static implicit operator PlatformRoute(PlatformId platformId) => platformId._route;
    public static implicit operator PlatformId(PlatformRoute route) => new(route);
    public static explicit operator PlatformId(string value) => Parse(value);
}
