using System.Net.Http.Json;
using System.Text.Json;

namespace Data.Statics;

/// <summary>
/// Maps a <c>major.minor</c> patch onto the Data Dragon folder that publishes it, shared by
/// every Data Dragon reader so they cannot disagree on which folder a patch reads.
/// </summary>
internal static class DataDragonVersions
{
    private const string VersionsUrl = "https://ddragon.leagueoflegends.com/api/versions.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The folder publishing <paramref name="patch"/>, or null when Data Dragon has not published it
    /// yet; the newest folder when <paramref name="patch"/> is null.
    /// </summary>
    public static async Task<string?> ResolveAsync(HttpClient httpClient, string? patch, CancellationToken ct)
    {
        var versions = await httpClient.GetFromJsonAsync<List<string>>(VersionsUrl, JsonOptions, ct) ?? [];
        if (versions.Count == 0)
        {
            throw new InvalidOperationException("Data Dragon returned no versions.");
        }

        return patch is null
            ? versions[0]
            : versions.FirstOrDefault(v => v.StartsWith(patch + ".", StringComparison.Ordinal));
    }
}
