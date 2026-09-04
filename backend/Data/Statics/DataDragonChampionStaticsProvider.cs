using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Lol.Patches;
using Microsoft.Extensions.Logging;

namespace Data.Statics;

/// <summary>
/// Reads champion statics from Data Dragon, once per patch per process: the version
/// list to map a <c>major.minor</c> patch onto the CDN's <c>major.minor.build</c>
/// folder, then <c>champion.json</c> for every champion's numeric key and attack range.
/// </summary>
/// <remarks>
/// Data Dragon publishes a patch hours after Riot ships it, so on patch day the first
/// games of the new patch can be folded while its folder does not exist yet. Like the
/// item-metadata provider's <c>latest</c> fallback, the newest published version is
/// used in that case: attack ranges are among the most stable numbers in the game, and
/// the alternative — no ranged flag for the whole first batch — is strictly worse. The
/// fallback is remembered for the life of the process; the flag is a <c>COALESCE</c>d
/// static column, so a later run on a later patch corrects nothing that matters.
/// A faulted load is evicted so the next batch retries instead of replaying the error.
/// </remarks>
public sealed class DataDragonChampionStaticsProvider(
    HttpClient httpClient,
    ILogger<DataDragonChampionStaticsProvider> logger) : IChampionStaticsProvider
{
    private const string VersionsUrl = "https://ddragon.leagueoflegends.com/api/versions.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyDictionary<int, ChampionStatics>>>> _cache =
        new(StringComparer.Ordinal);

    public async Task<IReadOnlyDictionary<int, ChampionStatics>> GetChampionsAsync(string gameVersion, CancellationToken ct)
    {
        var patch = PatchVersion.Parse(gameVersion).ToMajorMinor();

        var lazyTask = _cache.GetOrAdd(patch, static (normalizedPatch, provider) =>
            new Lazy<Task<IReadOnlyDictionary<int, ChampionStatics>>>(
                () => provider.LoadAsync(normalizedPatch, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication),
            this);

        try
        {
            return await lazyTask.Value.WaitAsync(ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ((ICollection<KeyValuePair<string, Lazy<Task<IReadOnlyDictionary<int, ChampionStatics>>>>>)_cache)
                .Remove(new KeyValuePair<string, Lazy<Task<IReadOnlyDictionary<int, ChampionStatics>>>>(patch, lazyTask));
            throw;
        }
    }

    private async Task<IReadOnlyDictionary<int, ChampionStatics>> LoadAsync(string patch, CancellationToken ct)
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
                "Data Dragon has not published patch {Patch} yet; reading champion statics from its newest version {Version}.",
                patch,
                version);
        }

        var url = $"https://ddragon.leagueoflegends.com/cdn/{version}/data/en_US/champion.json";
        await using var stream = await httpClient.GetStreamAsync(url, ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var champions = new Dictionary<int, ChampionStatics>();
        foreach (var champion in document.RootElement.GetProperty("data").EnumerateObject())
        {
            var value = champion.Value;
            if (!value.TryGetProperty("key", out var keyElement)
                || !int.TryParse(keyElement.GetString(), out var championId)
                || !value.TryGetProperty("stats", out var stats)
                || !stats.TryGetProperty("attackrange", out var rangeElement))
            {
                continue;
            }

            var attackRange = rangeElement.ValueKind == JsonValueKind.Number
                ? (int)Math.Round(rangeElement.GetDouble())
                : 0;
            champions[championId] = new ChampionStatics(championId, champion.Name, attackRange);
        }

        logger.LogInformation("Loaded statics for {Count} champions from Data Dragon {Version}.", champions.Count, version);
        return champions;
    }
}
