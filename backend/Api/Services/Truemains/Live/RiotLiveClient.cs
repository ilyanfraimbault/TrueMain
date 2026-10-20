using System.Net;
using Core;
using Core.Lol.Identifiers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace TrueMain.Services.Truemains.Live;

/// <summary>The two Riot reads the live tab needs (#2103).</summary>
public interface IRiotLiveClient
{
    /// <summary>spectator-v5: the game <paramref name="puuid"/> is playing right now.</summary>
    Task<RiotLiveResult<RiotActiveGameDto>> GetActiveGameAsync(string platformId, string puuid, CancellationToken ct);

    /// <summary>league-v4: every ranked entry of <paramref name="puuid"/> (an empty list when unranked).</summary>
    Task<RiotLiveResult<IReadOnlyList<RiotLiveLeagueEntryDto>>> GetLeagueEntriesAsync(
        string platformId,
        string puuid,
        CancellationToken ct);
}

public enum RiotLiveOutcome
{
    Found,

    /// <summary>A 404: for spectator-v5, "not in a game".</summary>
    NotFound,

    /// <summary>Not asked or no usable answer: no key, rate limited, Riot down, timed out.</summary>
    Unavailable,
}

public sealed record RiotLiveResult<T>(RiotLiveOutcome Outcome, T? Value)
{
    public static RiotLiveResult<T> NotFound { get; } = new(RiotLiveOutcome.NotFound, default);

    public static RiotLiveResult<T> Unavailable { get; } = new(RiotLiveOutcome.Unavailable, default);
}

/// <summary>Bound from the <c>Riot</c> section — the same <c>Riot__ApiKey</c> the ingestor reads.</summary>
public sealed class RiotLiveOptions
{
    public const string SectionName = "Riot";

    public string? ApiKey { get; set; }
}

/// <summary>
/// The API's only direct line to Riot. The ingestor owns every other Riot call and paces
/// them with its own limiter; this one has none, so it is built to ask as rarely as the
/// page allows: the callers cache every answer, and a 429 stops every call to that
/// platform for as long as Riot's <c>Retry-After</c> says, instead of adding pressure on
/// the quota the ingestor shares.
/// </summary>
public sealed class RiotLiveClient(
    HttpClient http,
    IOptions<RiotLiveOptions> options,
    IMemoryCache cache,
    ILogger<RiotLiveClient> logger) : IRiotLiveClient
{
    /// <summary>The wait after a 429 that carried no <c>Retry-After</c>.</summary>
    private static readonly TimeSpan DefaultBackoff = TimeSpan.FromSeconds(10);

    /// <summary>A ceiling on a <c>Retry-After</c>, so a malformed one cannot switch the tab off for hours.</summary>
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(2);

    public Task<RiotLiveResult<RiotActiveGameDto>> GetActiveGameAsync(
        string platformId,
        string puuid,
        CancellationToken ct)
        => GetAsync<RiotActiveGameDto>(
            platformId,
            $"/lol/spectator/v5/active-games/by-summoner/{Uri.EscapeDataString(puuid)}",
            ct);

    public async Task<RiotLiveResult<IReadOnlyList<RiotLiveLeagueEntryDto>>> GetLeagueEntriesAsync(
        string platformId,
        string puuid,
        CancellationToken ct)
    {
        var result = await GetAsync<List<RiotLiveLeagueEntryDto>>(
            platformId,
            $"/lol/league/v4/entries/by-puuid/{Uri.EscapeDataString(puuid)}",
            ct);
        return new RiotLiveResult<IReadOnlyList<RiotLiveLeagueEntryDto>>(result.Outcome, result.Value);
    }

    private async Task<RiotLiveResult<T>> GetAsync<T>(string platformId, string path, CancellationToken ct)
    {
        var apiKey = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)
            || !Enum.TryParse<PlatformRoute>(platformId, ignoreCase: true, out var platform)
            || !Enum.IsDefined(platform))
        {
            return RiotLiveResult<T>.Unavailable;
        }

        var host = platform.ToPlatformHost();
        var backoffKey = $"riot-live-backoff:{host}";
        if (cache.TryGetValue(backoffKey, out _))
        {
            return RiotLiveResult<T>.Unavailable;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://{host}.api.riotgames.com{path}"));
        request.Headers.Add("X-Riot-Token", apiKey);

        try
        {
            using var response = await http.SendAsync(request, ct);
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var value = await response.Content.ReadFromJsonAsync<T>(ct);
                    return value is null
                        ? RiotLiveResult<T>.Unavailable
                        : new RiotLiveResult<T>(RiotLiveOutcome.Found, value);

                case HttpStatusCode.NotFound:
                    return RiotLiveResult<T>.NotFound;

                case HttpStatusCode.TooManyRequests:
                    var wait = response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero
                        ? (delta < MaxBackoff ? delta : MaxBackoff)
                        : DefaultBackoff;
                    cache.Store(backoffKey, true, wait);
                    logger.LogWarning(
                        "Riot answered 429 to the live-game client on {Host}; pausing it for {Seconds}s",
                        host,
                        wait.TotalSeconds);
                    return RiotLiveResult<T>.Unavailable;

                default:
                    logger.LogWarning(
                        "Riot answered {StatusCode} to the live-game client on {Host}",
                        (int)response.StatusCode,
                        host);
                    return RiotLiveResult<T>.Unavailable;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "The live-game client could not read Riot on {Host}", host);
            return RiotLiveResult<T>.Unavailable;
        }
    }
}
