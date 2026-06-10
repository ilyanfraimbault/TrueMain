using Core.Lol.Identifiers;
using Ingestor.Riot.Dto;

namespace Ingestor.Riot;

public interface IRiotAccountClient
{
    Task<RiotAccountDto> GetAccountByPuuidAsync(string puuid, RegionalRoute region, CancellationToken ct);

    /// <summary>
    /// Resolves an account by its Riot ID (<paramref name="gameName"/> +
    /// <paramref name="tagLine"/>) via account-v1 on the regional host. Returns
    /// <see langword="null"/> when Riot reports the Riot ID does not exist (HTTP 404),
    /// so callers can distinguish "no such player" from a transport/auth failure
    /// (which still throws).
    /// </summary>
    Task<RiotAccountDto?> GetByRiotIdAsync(string gameName, string tagLine, RegionalRoute regional, CancellationToken ct);
}
