using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrueMain.TestKit;

/// <summary>The API's JSON settings, for a test that reads a response back into a read model.</summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
