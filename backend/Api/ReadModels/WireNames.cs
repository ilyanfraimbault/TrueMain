using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrueMain.ReadModels;

/// <summary>
/// An enum value as the API writes it (<c>"red"</c>, <c>"Failed"</c>), for the places that keep it as text
/// outside a response: Mongo documents, alert messages. Reads the same <c>JsonStringEnumMemberName</c>
/// attributes as the API's serializer, so the two never disagree.
/// </summary>
public static class WireNames
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    public static string WireName<TEnum>(this TEnum value)
        where TEnum : struct, Enum
        => JsonSerializer.Serialize(value, Options).Trim('"');

    /// <summary>The enum value named <paramref name="name"/>, or <paramref name="fallback"/> when none is.</summary>
    public static TEnum ParseWireName<TEnum>(string? name, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrEmpty(name))
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Deserialize<TEnum>(JsonSerializer.Serialize(name), Options);
        }
        catch (JsonException)
        {
            return fallback;
        }
    }
}
