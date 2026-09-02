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
        CancellationToken ct,
        bool profileResolved = true)
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
            // An unresolved profile can only reach here for an account the freshness probe
            // found in the database, so this branch always has a real summoner-v4 response.
            var created = new RiotAccount
            {
                Id = Guid.NewGuid(),
                Puuid = summoner.Puuid,
                PlatformId = platformId,
                SummonerId = summoner.Id,
                ProfileIconId = summoner.ProfileIconId,
                SummonerLevel = RiotValueConverters.ToIntSafe(summoner.SummonerLevel),
                UpdatedAtUtc = nowUtc,
                LastProfileSyncAtUtc = nowUtc
            };
            session.RiotAccounts.Add(created);
            return new AccountUpsertResult(IsNew: true, Account: created);
        }

        existing.PlatformId = platformId;
        existing.UpdatedAtUtc = nowUtc;

        // Nothing read this run, so nothing to write: the caller skipped summoner-v4 because
        // this row is already fresh (#1358). Writing the blanks of a synthesised DTO would
        // erase the cosmetics, and re-stamping LastProfileSyncAtUtc would make the row look
        // freshly read for ever — the freshness gate would then never reopen.
        if (!profileResolved)
        {
            return new AccountUpsertResult(IsNew: false, Account: existing);
        }

        existing.SummonerId = summoner.Id;
        existing.ProfileIconId = summoner.ProfileIconId;
        existing.SummonerLevel = RiotValueConverters.ToIntSafe(summoner.SummonerLevel);
        existing.LastProfileSyncAtUtc = nowUtc;
        return new AccountUpsertResult(IsNew: false, Account: existing);
    }
}
