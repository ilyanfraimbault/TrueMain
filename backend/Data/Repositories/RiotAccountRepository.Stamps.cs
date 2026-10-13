using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed partial class RiotAccountRepository
{
    // Set-based on purpose (#2046): a tracked stamp is checked against the row's xmin, which
    // the fetch lane's raw-SQL writes (LastSeenInMatchAtUtc, the summoner profile, the ingest
    // status) bump at any time, so the save matched 0 rows and failed the whole run. Each of
    // these columns has a single writer, so there is no concurrent write for xmin to protect.

    public Task<int> StampLastMainCalcAsync(
        IReadOnlyCollection<AccountKey> accounts, DateTime atUtc, CancellationToken ct)
        => StampAsync(accounts, rows => rows.ExecuteUpdateAsync(s => s.SetProperty(a => a.LastMainCalcAtUtc, atUtc), ct));

    public Task<int> StampLastActivityCheckAsync(
        IReadOnlyCollection<AccountKey> accounts, DateTime atUtc, CancellationToken ct)
        => StampAsync(accounts, rows => rows.ExecuteUpdateAsync(s => s.SetProperty(a => a.LastActivityCheckAtUtc, atUtc), ct));

    private async Task<int> StampAsync(
        IReadOnlyCollection<AccountKey> accounts, Func<IQueryable<RiotAccount>, Task<int>> update)
    {
        var stamped = 0;

        // One statement per platform, as in GetByKeysAsync.
        foreach (var grouping in accounts
                     .Distinct()
                     .GroupBy(a => a.PlatformId, StringComparer.OrdinalIgnoreCase))
        {
            var platformId = grouping.Key;
            var puuids = grouping.Select(a => a.Puuid).Distinct(StringComparer.Ordinal).ToList();
            stamped += await update(db.RiotAccounts.Where(a => a.PlatformId == platformId && puuids.Contains(a.Puuid)));
        }

        return stamped;
    }
}
