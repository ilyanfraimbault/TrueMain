using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Truemains.Identity;

namespace TrueMain.Services.Truemains.Live;

public interface ILiveGameQueryService
{
    /// <summary>
    /// The game the player behind <paramref name="nameTag"/> is playing right now, or null
    /// when TrueMain holds no such account (the controller's 404).
    /// </summary>
    /// <param name="nameTag">The player's Riot ID as <c>Name#TAG</c> or the <c>Name-TAG</c> slug.</param>
    /// <param name="refresh">
    /// The tab's "Check now": asks Riot again unless the answer on hand is younger than
    /// <see cref="LiveGameQueryService.MinRefreshInterval"/>.
    /// </param>
    /// <param name="ct">Request cancellation token.</param>
    Task<LiveGameReadModel?> GetAsync(string nameTag, bool refresh, CancellationToken ct);
}

/// <summary>
/// The live tab's read (#2103): spectator-v5 by PUUID, on demand only — a page open or a
/// "Check now", never a background poll.
///
/// <para>
/// <b>One Riot call per player per 30 s.</b> The answer, including "not in a game", is
/// cached per PUUID for <see cref="ResponseTtl"/>; concurrent viewers of one profile share
/// a single in-flight call. "Check now" may only bypass an answer older than
/// <see cref="MinRefreshInterval"/>, so a held-down button cannot turn into a stream of
/// calls on the quota the ingestor shares. An unavailable answer (Riot rate limited or
/// down) is kept only for that shorter interval, so the tab recovers quickly.
/// </para>
/// </summary>
public sealed class LiveGameQueryService(
    TruemainAccountResolver resolver,
    IRiotLiveClient riot,
    LiveGameHydrator hydrator,
    IMemoryCache cache,
    TimeProvider timeProvider) : ILiveGameQueryService
{
    public static readonly TimeSpan ResponseTtl = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(10);

    private static readonly RequestCoalescer<LiveGameReadModel> Coalescer = new();

    public async Task<LiveGameReadModel?> GetAsync(string nameTag, bool refresh, CancellationToken ct)
    {
        var account = await resolver.ResolveAsync(nameTag, ct);
        if (account is null)
        {
            return null;
        }

        var key = $"live-game:{account.Puuid}";
        if (cache.TryGetValue(key, out LiveGameReadModel? cached) && cached is not null
            && (!refresh || Now() - cached.CheckedAtUtc < MinRefreshInterval))
        {
            return cached;
        }

        // The pass borrows this request's scoped DbContext (the hydrator's), so the caller
        // that starts it must see it through, and it runs on no caller's token — see
        // RequestCoalescer. The Riot client's timeout and the command timeout bound it.
        return await Coalescer.GetOrJoinAsync(
            key,
            async () =>
            {
                var answer = await FetchAsync(account, CancellationToken.None);
                var ttl = answer.Status == LiveGameStatuses.Unavailable ? MinRefreshInterval : ResponseTtl;
                return cache.Store(key, answer, ttl);
            },
            ct,
            ownerAwaitsToCompletion: true);
    }

    private async Task<LiveGameReadModel> FetchAsync(TruemainAccountRef account, CancellationToken ct)
    {
        var checkedAt = Now();
        var result = await riot.GetActiveGameAsync(account.PlatformId, account.Puuid, ct);

        return result switch
        {
            { Outcome: RiotLiveOutcome.Found, Value: { } game } => new LiveGameReadModel
            {
                Status = LiveGameStatuses.InGame,
                CheckedAtUtc = checkedAt,
                Game = await hydrator.HydrateAsync(game, account, ct),
            },
            { Outcome: RiotLiveOutcome.NotFound } => new LiveGameReadModel
            {
                Status = LiveGameStatuses.NotInGame,
                CheckedAtUtc = checkedAt,
            },
            _ => new LiveGameReadModel
            {
                Status = LiveGameStatuses.Unavailable,
                CheckedAtUtc = checkedAt,
            },
        };
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
