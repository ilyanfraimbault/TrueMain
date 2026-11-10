using System.Text.Json;
using Core.Lol.Patches;
using Microsoft.Extensions.Logging;

namespace Data.Statics;

/// <summary>
/// Reads champion statics from Data Dragon's <c>champion.json</c>: every champion's numeric key,
/// attack range and attack / magic ratings, through the shared per-patch cache
/// (<see cref="PerPatchStaticCache{T}"/>). Attack ranges are among the most stable numbers in the
/// game, so the newest published version answering a patch-day request costs nothing.
/// </summary>
public sealed class DataDragonChampionStaticsProvider : IChampionStaticsProvider
{
    private readonly Func<HttpClient> _createClient;
    private readonly ILogger<DataDragonChampionStaticsProvider> _logger;
    private readonly PerPatchStaticCache<IReadOnlyDictionary<int, ChampionStatics>> _cache;

    public DataDragonChampionStaticsProvider(
        Func<HttpClient> createClient,
        ILogger<DataDragonChampionStaticsProvider> logger,
        TimeProvider timeProvider)
    {
        _createClient = createClient;
        _logger = logger;
        _cache = new(
            "Champion statics",
            (patch, ct) => LoadAsync(patch, ct),
            async ct => (await LoadAsync(null, ct))!,
            logger,
            timeProvider);
    }

    public Task<StaticData<IReadOnlyDictionary<int, ChampionStatics>>> GetChampionsAsync(
        string gameVersion,
        CancellationToken ct)
        => PatchVersion.TryParse(gameVersion, out var patch)
            ? _cache.GetAsync(patch.ToMajorMinor(), ct)
            : Task.FromResult(StaticData<IReadOnlyDictionary<int, ChampionStatics>>.Unavailable(
                $"'{gameVersion}' is not a patch."));

    private async Task<IReadOnlyDictionary<int, ChampionStatics>?> LoadAsync(string? patch, CancellationToken ct)
    {
        using var httpClient = _createClient();
        if (await DataDragonVersions.ResolveAsync(httpClient, patch, ct) is not { } version)
        {
            return null;
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
            champions[championId] = new ChampionStatics(
                championId,
                champion.Name,
                attackRange,
                Rating(value, "attack"),
                Rating(value, "magic"));
        }

        _logger.LogInformation("Loaded statics for {Count} champions from Data Dragon {Version}.", champions.Count, version);
        return champions;
    }

    /// <summary>One of the champion's <c>info</c> ratings, or null when Data Dragon does not carry it.</summary>
    private static int? Rating(JsonElement champion, string name)
        => champion.TryGetProperty("info", out var info)
            && info.TryGetProperty(name, out var rating)
            && rating.ValueKind == JsonValueKind.Number
                ? (int)Math.Round(rating.GetDouble())
                : null;
}
