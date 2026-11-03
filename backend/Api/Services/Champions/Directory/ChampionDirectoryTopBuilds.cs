using System.Data;
using System.Diagnostics;
using Core.Lol.Ranking;
using Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Builds;

namespace TrueMain.Services.Champions.Directory;

/// <summary>
/// The directory's top-build read: every grouped pattern of a patch, kept once per
/// aggregation version, and <see cref="Resolve"/> to fold it into one
/// <see cref="TopBuildReadModel"/> per <c>(champion, position)</c> row under the same elo
/// and population filters as the summaries it sits beside.
///
/// <para>
/// <b>One read per patch, not one per slice</b> (#2425). Each (patch, rank, population) used
/// to run the same scan with a different filter, and bring the same dimensions up with it:
/// cheap for one slice, seconds of Postgres times every slice the warm-up has to fill. Games
/// and wins add up across rank bands and populations, so the rows are grouped by those two
/// as well and a slice is a sum over the ones it selects, with no further SQL.
/// </para>
/// </summary>
internal sealed class ChampionDirectoryTopBuilds
{
    private const string Surface = ChampionSummariesQueryService.Surface;

    private const int PatternReadTimeoutSeconds = 180;

    private readonly Row[] rows;
    private readonly List<LaneKey> lanes;
    private readonly List<string> bands;
    private readonly IReadOnlyDictionary<Guid, BuildItems> builds;
    private readonly IReadOnlyDictionary<Guid, RuneChoice> runes;

    internal ChampionDirectoryTopBuilds(
        Builder rows,
        IReadOnlyDictionary<Guid, BuildItems> builds,
        IReadOnlyDictionary<Guid, RuneChoice> runes)
    {
        this.rows = [.. rows.Rows];
        lanes = rows.Lanes;
        bands = rows.Bands;
        this.builds = builds;
        this.runes = runes;
    }

    /// <summary>Reads every grouped pattern of <paramref name="activePatch"/> and the dimensions they name.</summary>
    public static async Task<ChampionDirectoryTopBuilds> LoadAsync(
        TrueMainDbContext db,
        int queueId,
        string activePatch,
        ILogger logger,
        CancellationToken ct)
    {
        var groupedSw = Stopwatch.StartNew();
        var grouped = await ReadPatternsAsync(db, queueId, activePatch, ct);
        groupedSw.Stop();
        logger.LogInformation(
            "{Surface} sql=patterns_join_grouped patch={Patch} buckets={Buckets} elapsed={ElapsedMs}ms",
            Surface, activePatch, grouped.Rows.Count, groupedSw.ElapsedMilliseconds);

        var buildIds = grouped.Rows.Select(row => row.BuildId).Distinct().ToList();
        var runeIds = grouped.Rows.Select(row => row.RunePageId).Distinct().ToList();

        var dimBuildsSw = Stopwatch.StartNew();
        var builds = await db.ChampionDimBuilds.AsNoTracking()
            .Where(dim => buildIds.Contains(dim.Id))
            .Select(dim => new
            {
                dim.Id,
                Items = new BuildItems(
                    dim.BuildItem0, dim.BuildItem1, dim.BuildItem2, dim.BuildItem3,
                    dim.BuildItem4, dim.BuildItem5, dim.BuildItem6),
            })
            .ToDictionaryAsync(dim => dim.Id, dim => dim.Items, ct);
        dimBuildsSw.Stop();
        logger.LogInformation(
            "{Surface} sql=dim_builds patch={Patch} rows={Rows} elapsed={ElapsedMs}ms",
            Surface, activePatch, builds.Count, dimBuildsSw.ElapsedMilliseconds);

        var dimRunesSw = Stopwatch.StartNew();
        var runes = await db.ChampionDimRunePages.AsNoTracking()
            .Where(dim => runeIds.Contains(dim.Id))
            .Select(dim => new { dim.Id, Choice = new RuneChoice(dim.PrimaryKeystoneId, dim.SecondaryStyleId) })
            .ToDictionaryAsync(dim => dim.Id, dim => dim.Choice, ct);
        dimRunesSw.Stop();
        logger.LogInformation(
            "{Surface} sql=dim_rune_pages patch={Patch} rows={Rows} elapsed={ElapsedMs}ms",
            Surface, activePatch, runes.Count, dimRunesSw.ElapsedMilliseconds);

        return new ChampionDirectoryTopBuilds(grouped, builds, runes);
    }

    /// <summary>
    /// Streams the rows straight off the connection: a patch holds a million or more of them,
    /// and going through EF would buffer them all under the API's retrying strategy and then
    /// materialise each one, strings included, for one array of a tenth of the size.
    /// </summary>
    /// <remarks>
    /// The one read allowed past the API's 60 s <c>statement_timeout</c>, in a transaction of
    /// its own so it stays that way on a pooled connection. It runs once per patch and version,
    /// and a smaller host takes close to a minute over it (#2425): chunking it per champion
    /// makes every statement short, at the price of a random heap read per row where this one
    /// scans the table.
    /// </remarks>
    private static async Task<Builder> ReadPatternsAsync(
        TrueMainDbContext db, int queueId, string activePatch, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = db.Database.GetDbConnection();
            await using var transaction = await connection.BeginTransactionAsync(ct);

            await using (var timeout = connection.CreateCommand())
            {
                timeout.Transaction = transaction;
                timeout.CommandText = $"SET LOCAL statement_timeout = '{PatternReadTimeoutSeconds}s'";
                await timeout.ExecuteNonQueryAsync(ct);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = PatternReadTimeoutSeconds + 5;
            command.CommandText = """
                SELECT s."ChampionId", s."Position", s.elo_bracket, s."IsMain", p."BuildId", p."RunePageId",
                       SUM(p."Games")::int, SUM(p."Wins")::int
                FROM champion_aggregate_patterns AS p
                INNER JOIN champion_aggregate_scopes AS s ON s."Id" = p."ScopeId"
                WHERE s."QueueId" = @queueId AND s."GameVersion" = @patch AND s."Position" <> ''
                GROUP BY s."ChampionId", s."Position", s.elo_bracket, s."IsMain", p."BuildId", p."RunePageId"
                """;
            command.Parameters.Add(new NpgsqlParameter("queueId", queueId));
            command.Parameters.Add(new NpgsqlParameter("patch", activePatch));

            var builder = new Builder();
            await using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    builder.Add(
                        reader.GetInt32(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetBoolean(3),
                        reader.GetGuid(4),
                        reader.GetGuid(5),
                        reader.GetInt32(6),
                        reader.GetInt32(7));
                }
            }

            await transaction.CommitAsync(ct);
            return builder;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// The dominant <c>(firstItem, primaryKeystone)</c> bucket of every
    /// <c>(champion, position)</c> pair under the filter, and the consensus item path for that
    /// bucket via <see cref="ChampionBuildPathAnalyzer"/> — the same tree-walk used to build the
    /// "core" path on the champion detail page, so the path shown on each list row matches the
    /// path on that champion's detail page for the same slice.
    /// </summary>
    /// <param name="bracketBands">
    /// Mirrors the summaries elo filter so the row's shown build matches the slice its WR / PR
    /// are computed from: null = ALL; a non-null set, empty included, restricts — see
    /// <see cref="EloBracket.ResolveFilterOrEmpty"/>.
    /// </param>
    /// <param name="truemainsOnly">Same population as the WR / PR beside it, for the same reason.</param>
    public IReadOnlyDictionary<(int ChampionId, string Position), TopBuildReadModel> Resolve(
        IReadOnlyList<string>? bracketBands,
        bool truemainsOnly)
    {
        var bandSelected = new bool[bands.Count];
        for (var band = 0; band < bands.Count; band++)
        {
            bandSelected[band] = bracketBands is null || bracketBands.Contains(bands[band], StringComparer.Ordinal);
        }

        // The sum over the selected bands and population, per lane and (build, rune) pair.
        var totals = new Dictionary<(int Lane, Guid BuildId, Guid RunePageId), (int Games, int Wins)>();
        foreach (ref readonly var row in rows.AsSpan())
        {
            if (!bandSelected[row.Band] || (truemainsOnly && !row.IsMain))
            {
                continue;
            }

            var key = (row.Lane, row.BuildId, row.RunePageId);
            var (games, wins) = totals.GetValueOrDefault(key);
            totals[key] = (games + row.Games, wins + row.Wins);
        }

        var result = new Dictionary<(int ChampionId, string Position), TopBuildReadModel>();

        foreach (var laneGroup in totals.GroupBy(entry => entry.Key.Lane))
        {
            // Hydrate the bucket rows with their dim entries. Skip any row
            // whose dim lookup is missing (transient state during ingest) or
            // whose build / rune is malformed.
            var enriched = laneGroup
                .Select(entry => new
                {
                    entry.Value.Games,
                    entry.Value.Wins,
                    Build = builds.TryGetValue(entry.Key.BuildId, out var build) ? build : (BuildItems?)null,
                    Rune = runes.TryGetValue(entry.Key.RunePageId, out var rune) ? rune : (RuneChoice?)null,
                })
                .Where(row => row.Build is not null && row.Rune is not null
                    && row.Build.Value.BuildItem0 > 0 && row.Rune.Value.PrimaryKeystoneId > 0)
                .ToList();

            if (enriched.Count == 0)
            {
                continue;
            }

            // Same first-tie ordering as ChampionBuildsQueryService: games
            // desc, firstItemId asc, keystoneId asc — so a champion's list
            // row and its detail page land on the same dominant bucket.
            var topBucket = enriched
                .GroupBy(row => (row.Build!.Value.BuildItem0, row.Rune!.Value.PrimaryKeystoneId))
                .Select(group => new
                {
                    FirstItem = group.Key.BuildItem0,
                    Keystone = group.Key.PrimaryKeystoneId,
                    Games = group.Sum(row => row.Games),
                    Wins = group.Sum(row => row.Wins),
                    Rows = group.ToList(),
                })
                .OrderByDescending(bucket => bucket.Games)
                .ThenBy(bucket => bucket.FirstItem)
                .ThenBy(bucket => bucket.Keystone)
                .First();

            // Consensus item path via the same tree-walk + threshold logic
            // the detail page uses for its core build path.
            var sequences = topBucket.Rows
                .Select(row => new ChampionBuildPathAnalyzer.BuildSequence(
                    row.Build!.Value.BuildItem1, row.Build.Value.BuildItem2, row.Build.Value.BuildItem3,
                    row.Build.Value.BuildItem4, row.Build.Value.BuildItem5, row.Build.Value.BuildItem6,
                    row.Games, row.Wins))
                .ToList();
            var tree = ChampionBuildPathAnalyzer.BuildItemTree(sequences, topBucket.Games);
            var (itemPath, _, _) = ChampionBuildPathAnalyzer.WalkPath(
                tree, topBucket.FirstItem, topBucket.Games, topBucket.Wins);

            // Dominant secondary tree within the top bucket.
            var secondaryStyleId = topBucket.Rows
                .GroupBy(row => row.Rune!.Value.SecondaryStyleId)
                .OrderByDescending(group => group.Sum(row => row.Games))
                .ThenBy(group => group.Key)
                .First().Key;

            var lane = lanes[laneGroup.Key];
            result[(lane.ChampionId, lane.Position)] = new TopBuildReadModel
            {
                FirstItemId = topBucket.FirstItem,
                PrimaryKeystoneId = topBucket.Keystone,
                SecondaryStyleId = secondaryStyleId,
                ItemPath = itemPath,
            };
        }

        return result;
    }

    /// <summary>
    /// The rows of a patch as they are read, with their lane and band interned: every row names
    /// them, and they are few.
    /// </summary>
    internal sealed class Builder
    {
        private readonly Dictionary<LaneKey, int> laneIndex = [];
        private readonly Dictionary<string, int> bandIndex = new(StringComparer.Ordinal);

        internal List<Row> Rows { get; } = [];

        internal List<LaneKey> Lanes { get; } = [];

        internal List<string> Bands { get; } = [];

        /// <summary>Takes the games a (build, rune) pair has on a lane, in one rank band and population.</summary>
        public void Add(
            int championId,
            string position,
            string eloBracket,
            bool isMain,
            Guid buildId,
            Guid runePageId,
            int games,
            int wins)
        {
            var lane = new LaneKey(championId, position);
            if (!laneIndex.TryGetValue(lane, out var laneNumber))
            {
                laneNumber = Lanes.Count;
                laneIndex[lane] = laneNumber;
                Lanes.Add(lane);
            }

            if (!bandIndex.TryGetValue(eloBracket, out var bandNumber))
            {
                bandNumber = Bands.Count;
                bandIndex[eloBracket] = bandNumber;
                Bands.Add(eloBracket);
            }

            Rows.Add(new Row(laneNumber, bandNumber, isMain, buildId, runePageId, games, wins));
        }
    }

    internal readonly record struct BuildItems(
        int BuildItem0, int BuildItem1, int BuildItem2, int BuildItem3, int BuildItem4, int BuildItem5, int BuildItem6);

    internal readonly record struct RuneChoice(int PrimaryKeystoneId, int SecondaryStyleId);

    internal readonly record struct LaneKey(int ChampionId, string Position);

    internal readonly record struct Row(int Lane, int Band, bool IsMain, Guid BuildId, Guid RunePageId, int Games, int Wins);
}
