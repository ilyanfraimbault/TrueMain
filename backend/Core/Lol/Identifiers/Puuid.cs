using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Lol.Identifiers;

/// <summary>
/// Value object wrapping a Riot encrypted PUUID — 78 base64url characters.
/// <para>
/// <c>default(Puuid)</c> is intentionally invalid: <see cref="Value"/>, the
/// implicit string conversion and <see cref="ToString"/> all throw rather than
/// returning an empty string, so a default never silently passes for a real id
/// (matching <see cref="PlatformId"/>).
/// </para>
/// </summary>
[JsonConverter(typeof(PuuidJsonConverter))]
public readonly record struct Puuid
    : IParsable<Puuid>, ISpanParsable<Puuid>, IComparable<Puuid>, IComparable
{
    // Riot encrypted PUUIDs are a fixed 78 characters from the base64url
    // alphabet. Kept as a named constant so the bound is easy to retune.
    private const int Length = 78;

    private readonly string? _value;

    public Puuid(string value)
    {
        if (!IsValidFormat(value))
        {
            throw new ArgumentException(
                $"Invalid PUUID: expected {Length} base64url characters.", nameof(value));
        }

        _value = value;
    }

    public string Value => _value ?? throw new InvalidOperationException("Default Puuid is not a valid identifier.");

    public static Puuid Parse(string value) => new(value);

    public static bool TryParse([NotNullWhen(true)] string? value, out Puuid puuid)
    {
        if (!IsValidFormat(value))
        {
            puuid = default;
            return false;
        }

        puuid = new Puuid(value);
        return true;
    }

    // IParsable / ISpanParsable: the format provider is ignored (a PUUID has no
    // culture-sensitive form). These delegate to the overloads above so generic
    // callers — minimal-API route binding, configuration, etc. — can build one.
    public static Puuid Parse(string s, IFormatProvider? provider) => Parse(s);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Puuid result)
        => TryParse(s, out result);

    public static Puuid Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => new(s.ToString());

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Puuid result)
        => TryParse(s.ToString(), out result);

    public int CompareTo(Puuid other) => string.CompareOrdinal(_value, other._value);

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        Puuid other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(Puuid)}.", nameof(obj)),
    };

    // Uniform with Value (the old ToString returned "" on default, which Value
    // never did): both now surface the invalid-default contract.
    public override string ToString() => Value;

    public static implicit operator string(Puuid puuid) => puuid.Value;
    public static explicit operator Puuid(string value) => new(value);

    private static bool IsValidFormat([NotNullWhen(true)] string? value)
    {
        if (value is null || value.Length != Length)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Serialises <see cref="Puuid"/> as its bare string form (and parses with the
/// same validation) instead of the record-struct's default object shape.
/// </summary>
internal sealed class PuuidJsonConverter : JsonConverter<Puuid>
{
    public override Puuid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // A non-nullable Puuid still routes a JSON null through here (the
        // serializer only short-circuits null for reference types and Puuid?).
        // Returning default(Puuid) would smuggle in the invalid sentinel whose
        // Value/ToString both throw on first use, so reject it at the boundary.
        var value = reader.GetString()
            ?? throw new JsonException("Puuid cannot be null.");
        return Puuid.Parse(value);
    }

    public override void Write(Utf8JsonWriter writer, Puuid value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
