using Core;
using Core.Lol.Identifiers;
using Data.Entities;
using Data.Repositories;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.Discovery;

public sealed class AccountUpsertService : IAccountUpsertService
{
    public async Task<AccountUpsertResult> UpsertAsync(
        IDataSession session,
        PlatformRoute platform,
        RiotSummonerDto summoner,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var existing = await session.RiotAccounts.GetByPuuidAsync(summoner.Puuid, ct);
        var platformId = platform.ToString();

        // GameName and TagLine are NOT owned by summoner-v4. Riot deprecated
        // summoner.name with the Riot ID rollout (it's empty on every modern
        // response), and summoner-v4 never exposed tagLine at all. The
        // authoritative source is account-v1, which AccountRefreshProcess
        // calls per-account. Touching either field here would clobber the
        // identity AccountRefresh resolved — keep them as-is on update, and
        // leave the entity defaults (empty string / null) on insert so the
        // next refresh cycle populates them.
        if (existing is null)
        {
            var created = new RiotAccount
            {
                Id = Guid.NewGuid(),
                Puuid = summoner.Puuid,
                PlatformId = platformId,
                SummonerId = summoner.Id,
                ProfileIconId = summoner.ProfileIconId,
                SummonerLevel = RiotDataHelpers.ToIntSafe(summoner.SummonerLevel),
                UpdatedAtUtc = nowUtc,
                LastProfileSyncAtUtc = nowUtc
            };
            session.RiotAccounts.Add(created);
            return new AccountUpsertResult(IsNew: true, Account: created);
        }

        existing.PlatformId = platformId;
        existing.SummonerId = summoner.Id;
        existing.ProfileIconId = summoner.ProfileIconId;
        existing.SummonerLevel = RiotDataHelpers.ToIntSafe(summoner.SummonerLevel);
        existing.UpdatedAtUtc = nowUtc;
        existing.LastProfileSyncAtUtc = nowUtc;
        return new AccountUpsertResult(IsNew: false, Account: existing);
    }
}
