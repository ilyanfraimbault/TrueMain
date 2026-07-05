// Dev-only tool: seeds a local Postgres with deterministic, realistic synthetic
// data across the full champion-stat read path (raw matches/participants/
// timeline snapshots/kill positions, the #606 matchup/lead pre-aggregation, and
// the Phase 6 build aggregation) so ChampionRoamQueryService, ChampionScaling-
// QueryService, ChampionMatchupQueryService, ChampionBuildsQueryService etc. all
// have something real to read locally, without waiting on the (rate-limited)
// Riot ingestion pipeline. See issue #631.
//
// Usage:
//   dotnet run --project backend/Tools/DevSeed -- [--games-per-champion N] [--patch X.Y] [--force]
//
// The connection string comes from (in order) an env var (ConnectionStrings__TrueMain),
// this project's own user secrets (`dotnet user-secrets set ConnectionStrings:TrueMain
// "..." --project backend/Tools/DevSeed`), or appsettings — same resolution order
// Api/Ingestor use, just against a separate secret id ("truemain-devseed") so this
// tool's config never collides with theirs.

using Data;
using Data.Entities;
using DevSeed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

const string DevSeedAccountPuuid = "DEVSEED0000000000000000000000000000000000000000000000000000001";

MapPoints.AssertValid();

var force = args.Contains("--force");
var gamesPerChampion = ReadIntArg(args, "--games-per-champion");
var patchArg = ReadStringArg(args, "--patch");

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddUserSecrets("truemain-devseed")
    .AddEnvironmentVariables()
    .Build();

var currentPatch = patchArg ?? configuration["DevSeed:CurrentPatch"] ?? "16.13";
var patchCount = int.Parse(configuration["DevSeed:PatchCount"] ?? "5");
var gamesPerPatch = (gamesPerChampion ?? int.Parse(configuration["DevSeed:GamesPerSlice"] ?? "80")) / patchCount;

var connectionString = configuration.GetConnectionString("TrueMain");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine(
        "Missing connection string. Set ConnectionStrings__TrueMain in the environment, or run:\n" +
        "  dotnet user-secrets set ConnectionStrings:TrueMain \"Host=localhost;Port=5432;Database=...;Username=...;Password=...\" --project backend/Tools/DevSeed");
    return 1;
}

if (!force && !LooksLocal(connectionString))
{
    Console.Error.WriteLine(
        "Refusing to run: the connection string doesn't look like a local dev database " +
        "(expected the host to be localhost/127.0.0.1/postgres/pgbouncer). " +
        "Pass --force if you're sure this is safe.");
    return 1;
}

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.EnableDynamicJson();
await using var dataSource = dataSourceBuilder.Build();

var options = new DbContextOptionsBuilder<TrueMainDbContext>()
    .UseNpgsql(dataSource)
    .Options;

await using var db = new TrueMainDbContext(options);

if (!force && await db.Matches.AnyAsync(m => !m.Id.StartsWith("DEVSEED_")))
{
    Console.Error.WriteLine(
        "Refusing to run: this database already has non-synthetic match data. " +
        "DevSeed truncates champion_matchup_stats / champion_timeline_lead_stats wholesale " +
        "(they carry no owner column to scope a delete to), which would destroy real aggregated " +
        "data. Pass --force if you're sure this database only ever holds DevSeed data.");
    return 1;
}

Console.WriteLine("Cleaning up previous DevSeed rows...");
await CleanupAsync(db);

var devSeedAccount = await GetOrCreateDevSeedAccountAsync(db);

var dimCache = new DimCache(db);
await dimCache.InitializeAsync();

Console.WriteLine($"Seeding {ChampionArchetypes.Seeds.Count} champion/position slices, " +
    $"{gamesPerPatch} games/patch x {patchCount} patches (patch {currentPatch})...");

var nowUtc = DateTime.UtcNow;
var totalMatches = 0;
foreach (var self in ChampionArchetypes.Seeds)
{
    var laneOpponents = ChampionArchetypes.Seeds.Where(s => s.Position == self.Position && s.Id != self.Id).ToList();
    if (laneOpponents.Count == 0)
    {
        laneOpponents = [self]; // single-champion lane pool fallback; never hit with the current seed list
    }

    var generator = new SeedGenerator(devSeedAccount, dimCache, currentPatch, patchCount, gamesPerPatch);
    var result = generator.Generate(self, laneOpponents, nowUtc);

    db.Matches.AddRange(result.Matches);
    db.MatchParticipants.AddRange(result.Participants);
    db.MatchParticipantTimelineSnapshots.AddRange(result.Snapshots);
    db.MatchParticipantKillPositions.AddRange(result.KillPositions);
    db.ChampionAggregateScopes.AddRange(result.Scopes);
    db.ChampionAggregatePatterns.AddRange(result.Patterns);
    db.ChampionMatchupStats.AddRange(result.MatchupStats);
    db.ChampionTimelineLeadStats.AddRange(result.LeadStats);

    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();

    totalMatches += result.Matches.Count;
    Console.WriteLine($"  {self.Position,-8} champion {self.Id,4} — {result.Matches.Count} games");
}

Console.WriteLine($"Done. {totalMatches} synthetic matches across {ChampionArchetypes.Seeds.Count} champion/position slices.");
return 0;

static int? ReadIntArg(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) ? value : null;
}

static string? ReadStringArg(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static bool LooksLocal(string connectionString)
{
    var builder = new NpgsqlConnectionStringBuilder(connectionString);
    string[] localHosts = ["localhost", "127.0.0.1", "postgres", "pgbouncer", "::1"];
    return localHosts.Contains(builder.Host, StringComparer.OrdinalIgnoreCase);
}

static async Task CleanupAsync(TrueMainDbContext db)
{
    // Participants first: their FK to matches is Restrict, so the match row
    // can't go while a participant still points at it. Deleting the match
    // afterward cascades its timeline snapshots and kill positions.
    await db.MatchParticipants.Where(p => p.MatchId.StartsWith("DEVSEED_")).ExecuteDeleteAsync();
    await db.Matches.Where(m => m.Id.StartsWith("DEVSEED_")).ExecuteDeleteAsync();

    // Scopes are owned by the reserved DevSeed account; deleting them cascades
    // their patterns. The shared Dim* tables are left alone — they're globally
    // deduplicated reference rows with no owner, so a few unused rows left
    // behind by a reseed are harmless.
    var devSeedAccountId = await db.RiotAccounts
        .Where(a => a.Puuid == DevSeedAccountPuuid)
        .Select(a => a.Id)
        .FirstOrDefaultAsync();
    if (devSeedAccountId != Guid.Empty)
    {
        await db.ChampionAggregateScopes.Where(s => s.RiotAccountId == devSeedAccountId).ExecuteDeleteAsync();
    }

    // No owner column exists on these two tables (see their doc comments) — the
    // --force / non-synthetic-data guard above is what keeps this safe.
    await db.ChampionMatchupStats.ExecuteDeleteAsync();
    await db.ChampionTimelineLeadStats.ExecuteDeleteAsync();
}

static async Task<RiotAccount> GetOrCreateDevSeedAccountAsync(TrueMainDbContext db)
{
    var existing = await db.RiotAccounts.FirstOrDefaultAsync(a => a.Puuid == DevSeedAccountPuuid);
    if (existing is not null)
    {
        return existing;
    }

    var account = new RiotAccount
    {
        Id = Guid.NewGuid(),
        Puuid = DevSeedAccountPuuid,
        GameName = "DevSeed",
        TagLine = "DEV",
        PlatformId = "EUW1",
        ProfileIconId = 1,
        SummonerLevel = 250,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
    };
    db.RiotAccounts.Add(account);
    await db.SaveChangesAsync();
    return account;
}
