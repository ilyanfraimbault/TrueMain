using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class RiotAccountRepository(TrueMainDbContext db) : IRiotAccountRepository
{
    public Task<RiotAccount?> GetByPuuidAsync(string puuid, CancellationToken ct)
        => db.RiotAccounts.FirstOrDefaultAsync(a => a.Puuid == puuid, ct);

    public Task<RiotAccount?> GetByKeyAsync(string platformId, string puuid, CancellationToken ct)
        => db.RiotAccounts.FirstOrDefaultAsync(a => a.PlatformId == platformId && a.Puuid == puuid, ct);

    public Task<List<RiotAccount>> GetAccountsForRefreshAsync(int batchSize, CancellationToken ct)
    {
        return db.RiotAccounts
            .OrderBy(a =>
                (a.GameName == null || a.GameName == string.Empty ||
                 a.TagLine == null || a.TagLine == string.Empty)
                    ? 0
                    : 1)
            .ThenBy(a => a.UpdatedAtUtc)
            .Take(Math.Max(1, batchSize))
            .ToListAsync(ct);
    }

    public Task<List<AccountKey>> GetAccountsForMainAnalysisAsync(DateTime cutoff, int batchSize, CancellationToken ct)
    {
        var accounts =
            from account in db.RiotAccounts
            join candidate in db.MainCandidates
                on new { account.PlatformId, account.Puuid } equals new { candidate.PlatformId, candidate.Puuid }
            where candidate.Status == MainCandidateStatus.Validated
            select account;

        if (cutoff > DateTime.MinValue)
        {
            accounts = accounts.Where(a => a.LastMainCalcAtUtc == null || a.LastMainCalcAtUtc < cutoff);
        }

        return accounts
            .Distinct()
            .OrderBy(a => a.LastMainCalcAtUtc == null ? 0 : 1)
            .ThenBy(a => a.LastMainCalcAtUtc)
            .Take(Math.Max(1, batchSize))
            .Select(a => new AccountKey(a.PlatformId, a.Puuid))
            .ToListAsync(ct);
    }

    public async Task<List<AccountKey>> ClaimAccountsForMatchIngestAsync(List<string> platforms, int batchSize, CancellationToken ct)
    {
        var queued = db.MainCandidates
            .Where(c => c.Status == MainCandidateStatus.Queued && platforms.Contains(c.PlatformId))
            .Select(c => new { c.PlatformId, c.Puuid })
            .Distinct();

        var mains = db.MainChampionStats
            .Where(s => s.IsMain && platforms.Contains(s.PlatformId))
            .Select(s => new { s.PlatformId, s.Puuid })
            .Distinct();

        var combined = queued.Union(mains);

        var selection = await (
                from account in combined
                join ra in db.RiotAccounts
                    on new { account.PlatformId, account.Puuid } equals new { ra.PlatformId, ra.Puuid }
                where ra.MatchIngestStatus == MatchIngestStatus.Idle
                orderby ra.LastMatchIngestAtUtc == null ? 0 : 1,
                    ra.LastMatchIngestAtUtc
                select new { account, ra }
            )
            .Take(Math.Max(1, batchSize))
            .ToListAsync(ct);

        foreach (var entry in selection)
        {
            if (entry.ra.MatchIngestStatus == MatchIngestStatus.Idle)
            {
                entry.ra.MatchIngestStatus = MatchIngestStatus.Processing;
            }
        }

        return selection
            .Select(s => new AccountKey(s.account.PlatformId, s.account.Puuid))
            .ToList();
    }

    public async Task<int> SetMatchIngestStatusAsync(string platformId, string puuid, MatchIngestStatus status, CancellationToken ct)
    {
        var account = await db.RiotAccounts
            .FirstOrDefaultAsync(a => a.PlatformId == platformId && a.Puuid == puuid, ct);

        if (account is null)
        {
            return 0;
        }

        account.MatchIngestStatus = status;
        return 1;
    }

    public async Task UpdateLastMatchIngestAtAsync(string platformId, string puuid, DateTime atUtc, CancellationToken ct)
    {
        var account = await db.RiotAccounts
            .FirstOrDefaultAsync(a => a.PlatformId == platformId && a.Puuid == puuid, ct);

        if (account is not null)
        {
            account.LastMatchIngestAtUtc = atUtc;
        }
    }

    public void Add(RiotAccount account)
        => db.RiotAccounts.Add(account);
}
