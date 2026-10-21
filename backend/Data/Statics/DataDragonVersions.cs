using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Data.Statics;

/// <summary>
/// Maps a <c>major.minor</c> patch onto the Data Dragon folder that publishes it, shared by
/// every Data Dragon reader so they cannot disagree on which folder a patch reads.
/// </summary>
/// <remarks>
/// Data Dragon publishes a patch hours after Riot ships it, so on patch day the folder may not
/// exist yet: the newest published version answers instead, and the caller is told so through
/// the log rather than failing a whole batch over a few new-patch games.
/// </remarks>
internal static class DataDragonVersions
{
    private const string VersionsUrl = "https://ddragon.leagueoflegends.com/api/versions.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<string> ResolveAsync(HttpClient httpClient, string patch, ILogger logger, string what, CancellationToken ct)
    {
        var versions = await httpClient.GetFromJsonAsync<List<string>>(VersionsUrl, JsonOptions, ct) ?? [];
        if (versions.Count == 0)
        {
            throw new InvalidOperationException("Data Dragon returned no versions.");
        }

        var version = versions.FirstOrDefault(v => v.StartsWith(patch + ".", StringComparison.Ordinal));
        if (version is null)
        {
            version = versions[0];
            logger.LogWarning(
                "Data Dragon has not published patch {Patch} yet; reading {What} from its newest version {Version}.",
                patch,
                what,
                version);
        }

        return version;
    }
}
