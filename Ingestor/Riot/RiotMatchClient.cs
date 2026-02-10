using Core;
using Ingestor.Options;
using Ingestor.Riot.Dto;
using Microsoft.Extensions.Options;

namespace Ingestor.Riot;

public sealed class RiotMatchClient : IRiotMatchClient
{
    private readonly HttpClient _httpClient;
    private readonly RiotOptions _options;
    private readonly IRiotHttpExecutor _httpExecutor;

    public RiotMatchClient(HttpClient httpClient, IOptions<RiotOptions> options, IRiotHttpExecutor httpExecutor)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpExecutor = httpExecutor;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Missing Riot ApiKey. Configure Riot:ApiKey.");
        }

        if (!_httpClient.DefaultRequestHeaders.Contains("X-Riot-Token"))
        {
            _httpClient.DefaultRequestHeaders.Add("X-Riot-Token", _options.ApiKey);
        }
    }

    public Task<RiotMatchDto> GetMatchAsync(string matchId, RegionalRoute region, CancellationToken ct)
    {
        var uri = BuildRegionalUri(region, $"/lol/match/v5/matches/{matchId}");
        return _httpExecutor.GetAsync<RiotMatchDto>(_httpClient, uri, _options.MaxRetryAttempts, nameof(RiotMatchClient), ct);
    }

    public async Task<MatchTimelineDto> GetTimelineAsync(string matchId, RegionalRoute region, CancellationToken ct)
    {
        var uri = BuildRegionalUri(region, $"/lol/match/v5/matches/{matchId}/timeline");
        var riotTimeline = await _httpExecutor.GetAsync<RiotTimelineDto>(_httpClient, uri, _options.MaxRetryAttempts, nameof(RiotMatchClient), ct);
        return MapTimeline(riotTimeline);
    }

    public Task<List<string>> GetMatchIdsAsync(string puuid, RegionalRoute region, int count, CancellationToken ct)
    {
        var safeCount = Math.Max(1, count);
        var uri = BuildRegionalUri(region, $"/lol/match/v5/matches/by-puuid/{puuid}/ids?count={safeCount}");
        return _httpExecutor.GetAsync<List<string>>(_httpClient, uri, _options.MaxRetryAttempts, nameof(RiotMatchClient), ct);
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
