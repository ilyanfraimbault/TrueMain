using Core;
using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.Discovery;

public sealed class CandidateUpsertService : ICandidateUpsertService
{
    public async Task<CandidateUpsertResult> UpsertAsync(
        IDataSession session,
        string platformId,
        string puuid,
        IReadOnlyCollection<RiotChampionMasteryDto> masteries,
        DiscoveryOptions options,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var topMasteries = masteries
            .OrderByDescending(mastery => mastery.ChampionPoints)
            .Take(Math.Max(0, options.TopChampionsPerAccount))
            .ToList();

        if (topMasteries.Count == 0)
        {
            return new CandidateUpsertResult(0, 0);
        }

        var championIds = topMasteries.Select(mastery => mastery.ChampionId).ToList();
        var existingCandidates = await session.MainCandidates
            .GetByPlatformPuuidAndChampionsAsync(platformId, puuid, championIds, ct);

        var existingByChampion = existingCandidates.ToDictionary(candidate => candidate.ChampionId);
        var inserted = 0;
        var updated = 0;

        for (var index = 0; index < topMasteries.Count; index++)
        {
            var mastery = topMasteries[index];
            var lastPlayUtc = RiotDataHelpers.ToUtcDateTime(mastery.LastPlayTime);
            if (lastPlayUtc is null)
            {
                continue;
            }

            if (options.MaxLastPlayDays > 0 && nowUtc - lastPlayUtc.Value > TimeSpan.FromDays(options.MaxLastPlayDays))
            {
                continue;
            }

            var rank = index + 1;

            if (existingByChampion.TryGetValue(mastery.ChampionId, out var existing))
            {
                existing.ChampionRankInMasteryTop = rank;
                existing.ChampionPoints = mastery.ChampionPoints;
                existing.LastPlayTimeUtc = lastPlayUtc.Value;
                existing.DiscoveredAtUtc = nowUtc;
                updated++;
                continue;
            }

            session.MainCandidates.Add(new MainCandidate
            {
                PlatformId = platformId,
                Puuid = puuid,
                ChampionId = mastery.ChampionId,
                ChampionRankInMasteryTop = rank,
                ChampionPoints = mastery.ChampionPoints,
                LastPlayTimeUtc = lastPlayUtc.Value,
                DiscoveredAtUtc = nowUtc
            });
            inserted++;
        }

        return new CandidateUpsertResult(inserted, updated);
    }
}
