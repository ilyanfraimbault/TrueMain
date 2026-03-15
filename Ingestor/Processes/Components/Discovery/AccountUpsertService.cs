using Core;
using Data.Entities;
using Data.Repositories;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.Discovery;

public sealed class AccountUpsertService : IAccountUpsertService
{
    public async Task<bool> UpsertAsync(
        IDataSession session,
        PlatformRoute platform,
        RiotSummonerDto summoner,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var existing = await session.RiotAccounts.GetByPuuidAsync(summoner.Puuid, ct);
        var platformId = platform.ToString();

        if (existing is null)
        {
            session.RiotAccounts.Add(new RiotAccount
            {
                Puuid = summoner.Puuid,
                GameName = summoner.Name,
                TagLine = null,
                PlatformId = platformId,
                SummonerId = summoner.Id,
                ProfileIconId = summoner.ProfileIconId,
                SummonerLevel = RiotDataHelpers.ToIntSafe(summoner.SummonerLevel),
                UpdatedAtUtc = nowUtc,
                LastProfileSyncAtUtc = nowUtc
            });
            return true;
        }

        existing.GameName = summoner.Name;
        existing.TagLine = null;
        existing.PlatformId = platformId;
        existing.SummonerId = summoner.Id;
        existing.ProfileIconId = summoner.ProfileIconId;
        existing.SummonerLevel = RiotDataHelpers.ToIntSafe(summoner.SummonerLevel);
        existing.UpdatedAtUtc = nowUtc;
        existing.LastProfileSyncAtUtc = nowUtc;
        return false;
    }
}
