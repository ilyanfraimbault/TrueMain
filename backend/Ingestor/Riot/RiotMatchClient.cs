using System.Net.Http.Json;
using Core;
using Core.Lol.Identifiers;
using Ingestor.Options;
using Ingestor.Riot.Dto;
using Microsoft.Extensions.Options;

namespace Ingestor.Riot;

public sealed class RiotMatchClient : IRiotMatchClient
{
    private readonly HttpClient _httpClient;

    public RiotMatchClient(HttpClient httpClient, IOptions<RiotOptions> options)
    {
        _httpClient = httpClient;
        var riotOptions = options.Value;

        if (string.IsNullOrWhiteSpace(riotOptions.ApiKey))
        {
            throw new InvalidOperationException("Missing Riot ApiKey. Configure Riot:ApiKey.");
        }

        if (!_httpClient.DefaultRequestHeaders.Contains("X-Riot-Token"))
        {
            _httpClient.DefaultRequestHeaders.Add("X-Riot-Token", riotOptions.ApiKey);
        }
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
        return MapTimeline(riotTimeline);
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

    private async Task<T> GetAsync<T>(Uri uri, CancellationToken ct)
    {
        return await _httpClient.GetFromJsonAsync<T>(uri, ct)
            ?? throw new InvalidOperationException($"Empty response from Riot API ({uri}).");
    }

    private static Uri BuildRegionalUri(RegionalRoute region, string path)
    {
        var host = RiotRouting.ToRegionalHost(region);
        return new Uri($"https://{host}.api.riotgames.com{path}");
    }

    private static MatchTimelineDto MapTimeline(RiotTimelineDto timeline)
    {
        var events = new List<MatchTimelineEventDto>();

        foreach (var frame in timeline.Info.Frames)
        {
            foreach (var evt in frame.Events)
            {
                if (evt.ParticipantId is null)
                {
                    continue;
                }

                events.Add(new MatchTimelineEventDto
                {
                    ParticipantId = evt.ParticipantId.Value,
                    TimestampMs = ToTimestamp(evt.Timestamp),
                    Type = evt.Type,
                    ItemId = evt.ItemId,
                    BeforeId = evt.BeforeId,
                    AfterId = evt.AfterId,
                    SkillSlot = evt.SkillSlot,
                    LevelUpType = evt.LevelUpType
                });
            }
        }

        return new MatchTimelineDto { Events = events };
    }

    private static int ToTimestamp(long timestamp)
    {
        if (timestamp <= 0)
        {
            return 0;
        }

        return timestamp > int.MaxValue ? int.MaxValue : (int)timestamp;
    }
}
