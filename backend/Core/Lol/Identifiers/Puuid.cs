namespace Core.Lol.Identifiers;

/// <summary>
/// Value object wrapping a Riot encrypted PUUID. Empty / whitespace strings are rejected.
/// </summary>
public readonly record struct Puuid
{
    private readonly string? _value;

    public Puuid(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("PUUID cannot be null, empty or whitespace.", nameof(value));
        }

        _value = value;
    }

    public string Value => _value ?? throw new InvalidOperationException("Default Puuid is not a valid identifier.");

    public static Puuid Parse(string value) => new(value);

    public static bool TryParse(string? value, out Puuid puuid)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            puuid = default;
            return false;
        }

        puuid = new Puuid(value);
        return true;
    }

    public override string ToString() => _value ?? string.Empty;

    public static implicit operator string(Puuid puuid) => puuid.Value;
    public static explicit operator Puuid(string value) => new(value);
}
