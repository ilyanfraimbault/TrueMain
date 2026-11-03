namespace TrueMain.Services.Champions.Warmup;

/// <summary>The champion cache warm-up (#2425), bound from <c>ChampionWarmup:*</c>.</summary>
public sealed class ChampionWarmupOptions
{
    public const string SectionName = "ChampionWarmup";

    /// <summary>
    /// Off by default, so a local API and the test hosts behave as they always did: the read
    /// cache follows the aggregation stamp and <c>/readyz</c> does not wait for anything.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How long <c>/readyz</c> holds the site behind the maintenance page for the first pass.
    /// Past it the site opens anyway, cold: a stuck warm-up must not become an outage.
    /// </summary>
    public int ReadyBudgetSeconds { get; set; } = 300;

    /// <summary>Requests a pass runs at once. Each one costs at most a Postgres connection.</summary>
    public int Concurrency { get; set; } = 4;

    /// <summary>How often the aggregation stamp is read to see whether a new pass is due.</summary>
    public int StampPollSeconds { get; set; } = 30;

    /// <summary>
    /// The shortest gap between two passes, whatever the stamp does: an aggregation lane that
    /// starts cycling faster must not turn the warm-up into a permanent scan.
    /// </summary>
    public int MinPassIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// A pass also runs when this long has gone by without one, so a lane that stops publishing
    /// leaves the live reads behind by this much at most. It has to stay under
    /// <see cref="WarmedEntryLifetime"/>, or the warmed entries expire before the next pass.
    /// </summary>
    public int MaxPassAgeMinutes { get; set; } = 90;

    /// <summary>The patches warmed, newest first. Older ones have no builds left to read.</summary>
    public int PatchDepth { get; set; } = 2;

    /// <summary>
    /// The rank a champion page opens on, which is what its per-champion reads are warmed for.
    /// Mirrors <c>DEFAULT_ELO_BRACKET</c> in the web app's <c>elo-brackets.ts</c>.
    /// </summary>
    public string DefaultEloBracket { get; set; } = "MASTER_PLUS";

    /// <summary>How long an entry computed by a pass may live: past the next pass, always.</summary>
    internal TimeSpan WarmedEntryLifetime => TimeSpan.FromMinutes(MaxPassAgeMinutes + 30);
}
