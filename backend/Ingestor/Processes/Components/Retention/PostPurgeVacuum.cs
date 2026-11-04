using System.Diagnostics;
using Data;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.Retention;

/// <summary>
/// The end of a <c>MatchDataRetentionProcess</c> run that deleted a mass of participants
/// (#2444). A platform's patch dropping out of the window clears the visibility map across
/// <c>match_participants</c>, and <c>ChampionGameCountRefreshProcess</c>, next in the lane,
/// reads that table through an index-only scan: until a vacuum has passed, that scan fetches
/// the heap for each index entry, competing with the autovacuum the purge just woke up.
/// Vacuuming here makes the lane wait for that work instead of racing it.
///
/// <para>
/// An autovacuum already running on the table is cancelled by Postgres in favour of this one,
/// which logs an error on the database side and is expected.
/// </para>
/// </summary>
public static class PostPurgeVacuum
{
    /// <summary>
    /// The bound on the vacuum. A pass over the participants of a whole retained window takes
    /// tens of minutes under load, far past the connection's 300 s.
    /// </summary>
    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(60);

    /// <summary>Returns whether it vacuumed.</summary>
    public static async Task<bool> RunIfMassDeleteAsync(
        IDbContextFactory<TrueMainDbContext> dbContextFactory,
        int deletedParticipants,
        int threshold,
        ILogger logger,
        CancellationToken ct)
    {
        if (threshold <= 0 || deletedParticipants < threshold)
        {
            return false;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        db.Database.SetCommandTimeout(CommandTimeout);

        // Held open so the setting and the vacuum share a session; VACUUM refuses to run in
        // the implicit transaction of a multi-statement batch.
        await db.Database.OpenConnectionAsync(ct);

        // A manual VACUUM runs unthrottled by default, which ingestion and the site would
        // feel: the same delay autovacuum runs under.
        await db.Database.ExecuteSqlRawAsync("SET vacuum_cost_delay = '2ms'", ct);

        var startedAt = Stopwatch.GetTimestamp();
        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) match_participants, matches", ct);

        logger.LogInformation(
            "Vacuumed the raw match tables in {Elapsed} after retention deleted {DeletedParticipants} participants.",
            Stopwatch.GetElapsedTime(startedAt),
            deletedParticipants);

        return true;
    }
}
