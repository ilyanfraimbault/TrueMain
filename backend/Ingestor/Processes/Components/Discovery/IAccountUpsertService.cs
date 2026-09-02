using Core.Lol.Identifiers;
using Data.Entities;
using Data.Repositories;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.Discovery;

public sealed record AccountUpsertResult(bool IsNew, RiotAccount Account);

public interface IAccountUpsertService
{
    /// <summary>
    /// Inserts or updates the account behind a ladder entry.
    /// </summary>
    /// <param name="session">Session the write is staged on.</param>
    /// <param name="platform">Platform the entry was read from.</param>
    /// <param name="summoner">Summoner-v4 response, or a PUUID-only stand-in — see <paramref name="profileResolved"/>.</param>
    /// <param name="nowUtc">Run timestamp used for the stamps.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="profileResolved">
    /// False when <paramref name="summoner"/> is not a summoner-v4 response but a PUUID lifted
    /// from the ladder entry, the call having been skipped as redundant (#1358). The cosmetics
    /// and <see cref="RiotAccount.LastProfileSyncAtUtc"/> are then left untouched instead of
    /// being overwritten with blanks and a stamp no call backs.
    /// </param>
    Task<AccountUpsertResult> UpsertAsync(
        IDataSession session,
        PlatformRoute platform,
        RiotSummonerDto summoner,
        DateTime nowUtc,
        CancellationToken ct,
        bool profileResolved = true);
}
