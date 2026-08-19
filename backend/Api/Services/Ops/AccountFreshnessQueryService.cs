using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public interface IAccountFreshnessQueryService
{
    /// <summary>
    /// Answers "do we already track these Riot IDs, and how recently did we ingest them?" for a
    /// whole batch, in one query.
    /// </summary>
    Task<IReadOnlyList<AccountFreshnessReadModel>> GetAsync(
        IReadOnlyList<AccountFreshnessQuery> requested,
        CancellationToken ct);
}

/// <summary>One Riot ID to look up.</summary>
public sealed record AccountFreshnessQuery(string GameName, string TagLine, string PlatformId);

/// <summary>
/// The bulk half of the account explorer (#1154).
///
/// <para>
/// It exists because the per-Riot-ID explorer is the wrong shape for a batch. That endpoint
/// traces one player through the whole pipeline, which is right for an operator and ruinous in
/// a loop: the OTP seeder's first run issued ~13.7k of those reads against production and
/// started collecting 30-second timeouts on the live site. Splitting the *question* rather than
/// paginating the answer keeps the explorer honest for its own job and gives a bulk caller a
/// query it can actually afford.
/// </para>
/// </summary>
public sealed class AccountFreshnessQueryService(TrueMainDbContext db) : IAccountFreshnessQueryService
{
    public async Task<IReadOnlyList<AccountFreshnessReadModel>> GetAsync(
        IReadOnlyList<AccountFreshnessQuery> requested,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requested);

        if (requested.Count == 0)
        {
            return [];
        }

        // Matched case-insensitively on the whole Riot ID. Riot IDs are not case-sensitive to a
        // player, and our stored spelling drifts from the live one until AccountRefresh catches
        // up — a case-sensitive match would report a tracked account as unknown and the caller
        // would pay a Riot call to rediscover it. The cost is that `lower(GameName)` cannot use
        // the (GameName, TagLine, PlatformId) index, so this scans; that is affordable because
        // the endpoint caps a request at BatchLimit rows and is called by batch jobs, not by a
        // page render.
        var names = requested
            .Select(entry => entry.GameName.Trim().ToLowerInvariant())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (names.Length == 0)
        {
            return [];
        }

        var rows = await db.RiotAccounts
            .AsNoTracking()
            .Where(account => names.Contains(account.GameName.ToLower()))
            .Select(account => new
            {
                account.GameName,
                account.TagLine,
                account.PlatformId,
                account.Status,
                account.LastMatchIngestAtUtc
            })
            .ToListAsync(ct);

        // The name filter alone is not the answer: a Riot ID is only unique within a platform,
        // and the same game name exists on several. Resolve the full triple here rather than
        // pushing three OR-ed IN lists into SQL.
        var byKey = new Dictionary<string, (RiotAccountStatus Status, DateTime? LastIngest)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var key = Key(row.GameName, row.TagLine ?? string.Empty, row.PlatformId);
            // A duplicate key would mean two accounts share a Riot ID on one platform, which the
            // unique puuid index does not forbid outright (a renamed account can collide until
            // AccountRefresh resolves it). Keep the most recently ingested one — that is the one
            // a caller would be asking about.
            if (!byKey.TryGetValue(key, out var existing)
                || (row.LastMatchIngestAtUtc ?? DateTime.MinValue) > (existing.LastIngest ?? DateTime.MinValue))
            {
                byKey[key] = (row.Status, row.LastMatchIngestAtUtc);
            }
        }

        return requested
            .Select(entry =>
            {
                var found = byKey.TryGetValue(
                    Key(entry.GameName, entry.TagLine, entry.PlatformId), out var match);

                return new AccountFreshnessReadModel
                {
                    GameName = entry.GameName,
                    TagLine = entry.TagLine,
                    PlatformId = entry.PlatformId,
                    Known = found,
                    Status = found ? match.Status.ToString() : null,
                    LastMatchIngestAtUtc = found ? match.LastIngest : null
                };
            })
            .ToList();
    }

    private static string Key(string gameName, string tagLine, string platformId)
        => $"{platformId.Trim()}|{gameName.Trim()}|{tagLine.Trim()}";
}
