using System.Data;
using System.Diagnostics;
using Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TrueMain.Services.Champions.Directory;

/// <summary>
/// The aggregated figures of a patch's <c>(champion, position)</c> groups: every scope row of the
/// patch, kept once per aggregation version, and <see cref="Resolve"/> to fold them under an elo
/// and population filter, as <see cref="ChampionDirectoryTopBuilds"/> does for the builds and for
/// the same reason (#2425). The mains are distinct accounts, so they are counted per filter.
/// </summary>
internal sealed class ChampionDirectoryScopeGroups
{
    private const string Surface = ChampionSummariesQueryService.Surface;

    // Grouped by lane, so a filter folds each one in a walk over the array.
    private readonly List<Row>[] rowsByLane;
    private readonly List<LaneKey> lanes;
    private readonly List<string> bands;

    internal ChampionDirectoryScopeGroups(Builder source)
    {
        rowsByLane = source.RowsByLane;
        lanes = source.Lanes;
        bands = source.Bands;
    }

    /// <summary>Reads every scope row of <paramref name="activePatch"/>.</summary>
    public static async Task<ChampionDirectoryScopeGroups> LoadAsync(
        TrueMainDbContext db,
        int queueId,
        string activePatch,
        ILogger logger,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var builder = new Builder();

        // Straight off the connection, like the patterns: see ChampionDirectoryTopBuilds.
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT "ChampionId", "Position", elo_bracket, "IsMain", "RiotAccountId", "Games", "Wins", "AggregatedAtUtc"
                FROM champion_aggregate_scopes
                WHERE "QueueId" = @queueId AND "GameVersion" = @patch
                """;
            command.Parameters.Add(new NpgsqlParameter("queueId", queueId));
            command.Parameters.Add(new NpgsqlParameter("patch", activePatch));

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
            while (await reader.ReadAsync(ct))
            {
                builder.Add(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetBoolean(3),
                    reader.GetGuid(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetDateTime(7));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        logger.LogInformation(
            "{Surface} sql=scope_rows patch={Patch} lanes={Lanes} elapsed={ElapsedMs}ms",
            Surface, activePatch, builder.Lanes.Count, sw.ElapsedMilliseconds);
        return new ChampionDirectoryScopeGroups(builder);
    }

    /// <summary>
    /// One group per lane the filter selects anything on, position-less scopes included: they
    /// count towards the patch's total without being a line of the directory.
    /// </summary>
    /// <param name="bracketBands">
    /// Null is ALL (the full union, Unranked included); a non-null set restricts to those bands,
    /// empty included, which matches nothing rather than widening back to ALL for a rejected filter.
    /// </param>
    /// <param name="truemainsOnly">Mains of the champion only, or every tracked player with games on it.</param>
    public IReadOnlyList<Group> Resolve(IReadOnlyList<string>? bracketBands, bool truemainsOnly)
    {
        var bandSelected = new bool[bands.Count];
        for (var band = 0; band < bands.Count; band++)
        {
            bandSelected[band] = bracketBands is null || bracketBands.Contains(bands[band], StringComparer.Ordinal);
        }

        var groups = new List<Group>(rowsByLane.Length);
        var mains = new HashSet<int>();
        for (var lane = 0; lane < rowsByLane.Length; lane++)
        {
            mains.Clear();
            var (games, wins, selected) = (0, 0, false);
            var latest = DateTime.MinValue;
            foreach (var row in rowsByLane[lane])
            {
                if (!bandSelected[row.Band] || (truemainsOnly && !row.IsMain))
                {
                    continue;
                }

                selected = true;
                games += row.Games;
                wins += row.Wins;
                latest = row.AggregatedAtUtc > latest ? row.AggregatedAtUtc : latest;

                // Counts mains whatever the population filter is, so the field keeps meaning
                // what its name says: under `truemainsOnly: false` an unqualified distinct count
                // would be "tracked players", a different number wearing the truemain label.
                if (row.IsMain)
                {
                    mains.Add(row.Account);
                }
            }

            if (selected)
            {
                groups.Add(new Group(lanes[lane].ChampionId, lanes[lane].Position, games, wins, mains.Count, latest));
            }
        }

        return groups;
    }

    /// <summary>What the directory reads of one <c>(champion, position)</c> under a filter.</summary>
    internal sealed record Group(
        int ChampionId,
        string Position,
        int Games,
        int Wins,
        int TrueMainCount,
        DateTime LastUpdatedAtUtc);

    /// <summary>The scope rows of a patch as they are read, with what they name interned.</summary>
    internal sealed class Builder
    {
        private readonly Dictionary<LaneKey, int> laneIndex = [];
        private readonly Dictionary<string, int> bandIndex = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, int> accountIndex = [];
        private readonly List<List<Row>> rowsByLane = [];

        internal List<Row>[] RowsByLane => [.. rowsByLane];

        internal List<LaneKey> Lanes { get; } = [];

        internal List<string> Bands { get; } = [];

        public void Add(
            int championId,
            string position,
            string eloBracket,
            bool isMain,
            Guid accountId,
            int games,
            int wins,
            DateTime aggregatedAtUtc)
        {
            var key = new LaneKey(championId, position);
            if (!laneIndex.TryGetValue(key, out var lane))
            {
                lane = Lanes.Count;
                laneIndex[key] = lane;
                Lanes.Add(key);
                rowsByLane.Add([]);
            }

            if (!bandIndex.TryGetValue(eloBracket, out var band))
            {
                band = Bands.Count;
                bandIndex[eloBracket] = band;
                Bands.Add(eloBracket);
            }

            if (!accountIndex.TryGetValue(accountId, out var account))
            {
                account = accountIndex.Count;
                accountIndex[accountId] = account;
            }

            rowsByLane[lane].Add(new Row(band, isMain, account, games, wins, aggregatedAtUtc));
        }
    }

    internal readonly record struct LaneKey(int ChampionId, string Position);

    internal readonly record struct Row(int Band, bool IsMain, int Account, int Games, int Wins, DateTime AggregatedAtUtc);
}
