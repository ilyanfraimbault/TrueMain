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
        //
        // The two sides lower-case through different implementations — ToLowerInvariant here,
        // Postgres `lower()` there — which can disagree on a few non-ASCII characters (the
        // Turkish dotted I being the usual example). Left as is rather than contorted around,
        // because the failure is bounded and one-directional: a disagreement can only make us
        // miss a row we hold, which reports the account as unknown and costs the caller one
        // redundant seed. It can never invent an account we do not have, nor report a stale one
        // as fresh — the two answers acting on it would get wrong.
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
        //
        // Keyed on the triple itself, not on a joined string: any separator would be a
        // character a game name might one day contain, and a collision here silently merges two
        // different players' answers. A tuple cannot collide, so the question does not arise.
        var byKey = new Dictionary<(string, string, string), (RiotAccountStatus Status, DateTime? LastIngest)>(
            PlatformRiotIdComparer.Instance);
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

    private static (string PlatformId, string GameName, string TagLine) Key(
        string gameName, string tagLine, string platformId)
        => (platformId.Trim(), gameName.Trim(), tagLine.Trim());

    /// <summary>
    /// Compares a (platform, gameName, tagLine) triple case-insensitively on all three parts.
    /// </summary>
    private sealed class PlatformRiotIdComparer : IEqualityComparer<(string PlatformId, string GameName, string TagLine)>
    {
        public static readonly PlatformRiotIdComparer Instance = new();

        public bool Equals(
            (string PlatformId, string GameName, string TagLine) x,
            (string PlatformId, string GameName, string TagLine) y)
            => StringComparer.OrdinalIgnoreCase.Equals(x.PlatformId, y.PlatformId)
               && StringComparer.OrdinalIgnoreCase.Equals(x.GameName, y.GameName)
               && StringComparer.OrdinalIgnoreCase.Equals(x.TagLine, y.TagLine);

        public int GetHashCode((string PlatformId, string GameName, string TagLine) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PlatformId),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.GameName),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TagLine));
    }
}
