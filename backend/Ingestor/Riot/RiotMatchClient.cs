using System.Globalization;
using System.Text;
using Core;
using Core.Lol.Identifiers;
using Ingestor.Riot.Dto;

namespace Ingestor.Riot;

public sealed class RiotMatchClient : IRiotMatchClient
{
    private readonly HttpClient _httpClient;

    public RiotMatchClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<RiotMatchDto> GetMatchAsync(string matchId, RegionalRoute region, CancellationToken ct)
    {
        var uri = BuildRegionalUri(region, $"/lol/match/v5/matches/{matchId}");
        return GetAsync<RiotMatchDto>(uri, ct);
    }

    public async Task<MatchTimelineDto> GetTimelineAsync(string matchId, RegionalRoute region, CancellationToken ct)
    {
        var uri = BuildRegionalUri(region, $"/lol/match/v5/matches/{matchId}/timeline");
        var riotTimeline = await GetAsync<RiotTimelineDto>(uri, ct);
        return RiotTimelineMapper.Map(riotTimeline);
    }

    public Task<List<string>> GetMatchIdsAsync(MatchIdQuery query, CancellationToken ct)
    {
        // Riot caps count at 100 and 400s above it, so clamping here lets
        // MatchIngestion:MatchesPerAccount stay the authoritative knob: raise it to 100 and a
        // very active main stops being truncated at 20 ids per claim.
        var safeCount = Math.Clamp(query.Count, 1, 100);

        // type=ranked filters at the source so the ingestor never burns
        // requests fetching Arena / ARAM / normal / co-op-vs-AI rounds —
        // those modes are not used by any downstream surface (champion
        // aggregates use queue 420 only, the truemain match feed wants
        // ranked play). Saves both Riot API rate and the per-match
        // /matches/{id} round trip MatchSnapshotWriter would do for each
        // returned id.
        var uri = new StringBuilder("/lol/match/v5/matches/by-puuid/")
            .Append(query.Puuid)
            .Append("/ids?count=")
            .Append(safeCount.ToString(CultureInfo.InvariantCulture))
            .Append("&type=ranked");

        // queue narrows type=ranked further — Riot ANDs the two — so flex (440) is never
        // listed, never fetched, and never re-listed on the next claim. Without it a flex id
        // costs one /matches/{id} on every single claim of that account, for ever: nothing is
        // stored for it, so ExistingMatchScanner sees it as new every time (#1358).
        if (query.QueueId is { } queueId)
        {
            uri.Append("&queue=").Append(queueId.ToString(CultureInfo.InvariantCulture));
        }

        // startTime is epoch *seconds*. Riot only applies it to matches played after
        // 2021-06-16 (games before that carry no start timestamp in the index) — irrelevant
        // here, where the window is at most a few hours wide.
        if (query.StartTimeUtc is { } startTimeUtc)
        {
            var epochSeconds = new DateTimeOffset(DateTime.SpecifyKind(startTimeUtc, DateTimeKind.Utc))
                .ToUnixTimeSeconds();
            uri.Append("&startTime=").Append(epochSeconds.ToString(CultureInfo.InvariantCulture));
        }

        return GetAsync<List<string>>(BuildRegionalUri(query.Region, uri.ToString()), ct);
    }

    private Task<T> GetAsync<T>(Uri uri, CancellationToken ct)
    {
        return _httpClient.GetFromJsonStreamingAsync<T>(uri, ct);
    }

    private static Uri BuildRegionalUri(RegionalRoute region, string path)
    {
        var host = region.ToRegionalHost();
        return new Uri($"https://{host}.api.riotgames.com{path}");
    }
}
