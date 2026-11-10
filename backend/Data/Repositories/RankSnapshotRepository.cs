using Core.Lol.Ranking;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class RankSnapshotRepository(TrueMainDbContext db) : IRankSnapshotRepository
{
    private readonly List<RankSnapshot> _staged = [];

    public void Add(RankSnapshot snapshot)
        => _staged.Add(snapshot);

    /// <summary>
    /// Writes the snapshots staged by <see cref="Add"/> as one upsert on the
    /// one-row-per-account-per-day index, then forgets them. Called by
    /// <see cref="DataSession.SaveChangesAsync"/> after the tracked changes are saved, so an
    /// account inserted in the same save exists before its snapshot references it.
    /// </summary>
    /// <remarks>
    /// Discovery, LadderSync and AccountRefresh each decide insert-or-update against a latest
    /// snapshot they preloaded earlier, so another writer can fill the day in between (#2587).
    /// A plain insert then broke <c>IX_rank_snapshots_account_day</c> and failed the whole save
    /// slice; here the newest reading of the day wins, whichever writer holds it.
    /// </remarks>
    internal async Task<int> SaveStagedAsync(CancellationToken ct)
    {
        if (_staged.Count == 0)
        {
            return 0;
        }

        var rows = NewestPerAccountDay(_staged);
        _staged.Clear();

        var ids = rows.Select(row => row.Id == Guid.Empty ? Guid.NewGuid() : row.Id).ToArray();
        var accountIds = rows.Select(row => row.RiotAccountId).ToArray();
        var capturedAt = rows.Select(row => AsUtc(row.CapturedAtUtc)).ToArray();
        var tiers = rows.Select(row => row.Tier.ToRiotName()).ToArray();
        var divisions = rows.Select(row => row.Division.ToRiotName()).ToArray();
        var leaguePoints = rows.Select(row => row.LeaguePoints).ToArray();
        var wins = rows.Select(row => row.Wins).ToArray();
        var losses = rows.Select(row => row.Losses).ToArray();

        // The conflict target is the expression of IX_rank_snapshots_account_day
        // (DedupeRankSnapshotsPerDay), spelled the same so Postgres infers that index.
        return await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO rank_snapshots
                ("Id", "RiotAccountId", "CapturedAtUtc", "Tier", "Division", "LeaguePoints", "Wins", "Losses")
            SELECT * FROM unnest({ids}, {accountIds}, {capturedAt}, {tiers}, {divisions}, {leaguePoints}, {wins}, {losses})
            ON CONFLICT ("RiotAccountId", (date_trunc('day', "CapturedAtUtc" AT TIME ZONE 'UTC')))
            DO UPDATE SET
                "CapturedAtUtc" = EXCLUDED."CapturedAtUtc",
                "Tier" = EXCLUDED."Tier",
                "Division" = EXCLUDED."Division",
                "LeaguePoints" = EXCLUDED."LeaguePoints",
                "Wins" = EXCLUDED."Wins",
                "Losses" = EXCLUDED."Losses"
            WHERE rank_snapshots."CapturedAtUtc" <= EXCLUDED."CapturedAtUtc"
            """,
            ct);
    }

    /// <summary>Forgets the staged snapshots, as clearing the change tracker forgets tracked ones.</summary>
    internal void DiscardStaged() => _staged.Clear();

    /// <summary>
    /// One row per account per UTC day, the newest — one upsert statement cannot touch the same
    /// row twice, and the index would keep only one of them anyway.
    /// </summary>
    internal static List<RankSnapshot> NewestPerAccountDay(IEnumerable<RankSnapshot> snapshots)
        => snapshots
            .GroupBy(snapshot => (snapshot.RiotAccountId, AsUtc(snapshot.CapturedAtUtc).Date))
            .Select(day => day.MaxBy(snapshot => snapshot.CapturedAtUtc)!)
            .ToList();

    private static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public void Update(RankSnapshot snapshot)
        => db.RankSnapshots.Update(snapshot);

    public Task<RankSnapshot?> GetLatestAsync(Guid riotAccountId, CancellationToken ct)
        => db.RankSnapshots
            .AsNoTracking()
            .Where(s => s.RiotAccountId == riotAccountId)
            .OrderByDescending(s => s.CapturedAtUtc)
            .FirstOrDefaultAsync(ct);

    public async Task<Dictionary<Guid, RankSnapshot>> GetLatestForAccountsAsync(
        IReadOnlyCollection<Guid> riotAccountIds,
        CancellationToken ct)
    {
        if (riotAccountIds.Count == 0)
        {
            return new Dictionary<Guid, RankSnapshot>();
        }

        var ids = riotAccountIds.Distinct().ToList();

        var latest = await db.RankSnapshots
            .AsNoTracking()
            .Where(s => ids.Contains(s.RiotAccountId))
            .GroupBy(s => s.RiotAccountId)
            .Select(g => g.OrderByDescending(s => s.CapturedAtUtc).First())
            .ToListAsync(ct);

        return latest.ToDictionary(s => s.RiotAccountId);
    }

    public async Task<Dictionary<Guid, List<(DateTime CapturedAtUtc, string? Tier)>>> GetTierHistoryForAccountsAsync(
        IReadOnlyCollection<Guid> riotAccountIds,
        CancellationToken ct)
    {
        if (riotAccountIds.Count == 0)
        {
            return [];
        }

        var ids = riotAccountIds.Distinct().ToList();

        var captures = await db.RankSnapshots
            .AsNoTracking()
            .Where(s => ids.Contains(s.RiotAccountId))
            .Select(s => new { s.RiotAccountId, s.CapturedAtUtc, s.Tier })
            .ToListAsync(ct);

        return captures
            .GroupBy(capture => capture.RiotAccountId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(capture => (capture.CapturedAtUtc, (string?)capture.Tier.ToRiotName())).ToList());
    }

    public Task<List<RankSnapshot>> GetHistoryAsync(
        Guid riotAccountId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct)
        => db.RankSnapshots
            .AsNoTracking()
            .Where(s => s.RiotAccountId == riotAccountId
                        && s.CapturedAtUtc >= fromUtc
                        && s.CapturedAtUtc <= toUtc)
            .OrderBy(s => s.CapturedAtUtc)
            .ToListAsync(ct);
}
