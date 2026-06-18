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

    public Task<List<string>> GetMatchIdsAsync(string puuid, RegionalRoute region, int count, CancellationToken ct)
    {
        var safeCount = Math.Max(1, count);
        // type=ranked filters at the source so the ingestor never burns
        // requests fetching Arena / ARAM / normal / co-op-vs-AI rounds —
        // those modes are not used by any downstream surface (champion
        // aggregates use queue 420 only, the truemain match feed wants
        // ranked play). Saves both Riot API rate and the per-match
        // /matches/{id} round trip MatchSnapshotWriter would do for each
        // returned id.
        var uri = BuildRegionalUri(region, $"/lol/match/v5/matches/by-puuid/{puuid}/ids?count={safeCount}&type=ranked");
        return GetAsync<List<string>>(uri, ct);
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
