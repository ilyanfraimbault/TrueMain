using Core.Lol.Identifiers;
using Data.Entities;
using Data.Repositories;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.Discovery;

public sealed record AccountUpsertResult(bool IsNew, RiotAccount Account);

public interface IAccountUpsertService
{
    Task<AccountUpsertResult> UpsertAsync(
        IDataSession session,
        PlatformRoute platform,
        RiotSummonerDto summoner,
        DateTime nowUtc,
        CancellationToken ct);
}
