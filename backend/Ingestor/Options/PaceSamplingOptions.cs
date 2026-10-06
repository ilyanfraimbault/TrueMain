using System.ComponentModel.DataAnnotations;

namespace Ingestor.Options;

/// <summary>
/// The low-tier pace sampler (#1912): how many Riot calls it may spend reading games of the
/// tiers TrueMain does not otherwise ingest, folded into the pace benchmark and never stored.
/// </summary>
public class PaceSamplingOptions
{
    public const string SectionName = "PaceSampling";

    /// <summary>
    /// Optional narrowing override of the shared <c>Platforms:Active</c> list, with the same
    /// contract as <see cref="LadderSyncOptions.Platforms"/>: empty inherits the shared list.
    /// </summary>
    public List<string> Platforms { get; set; } = [];

    /// <summary>
    /// The tiers sampled, read through league-v4's paginated per-division ladder (so never an
    /// apex tier). Empty by default for the binder's append reason (see
    /// <see cref="LadderSyncOptions.TierScope"/>); the shipped value lives in <c>appsettings.json</c>.
    /// An empty list disables the sampler.
    /// </summary>
    public List<string> TierScope { get; set; } = [];

    /// <summary>
    /// Ceiling on the Riot calls one run may spend — every call counts: the ladder page, the
    /// match-id list, the match and its timeline. 0 disables the sampler.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int MaxRequestsPerRun { get; set; }

    /// <summary>
    /// Ceiling on the Riot calls spent per UTC day across every run, read back from the run
    /// summaries like <see cref="LadderSyncOptions.MaxRequestsPerDay"/>. 0 disables the daily
    /// ceiling (the per-run cap still holds).
    /// </summary>
    [Range(0, int.MaxValue)]
    public int MaxRequestsPerDay { get; set; }

    /// <summary>
    /// Minimum interval between two runs that did their work; <see cref="TimeSpan.Zero"/> runs
    /// it every fetch-lane iteration.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "10675199.02:48:05.4775807")]
    public TimeSpan MinRunInterval { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Highest ladder page drawn at random for a division. A random page keeps the sampler off
    /// the same top-of-division players; a page past a division's end comes back empty and is
    /// simply a spent call.
    /// </summary>
    [Range(1, 1000)]
    public int MaxLadderPage { get; set; } = 30;

    /// <summary>Players drawn from one ladder page as seeds.</summary>
    [Range(1, 50)]
    public int SeedsPerPage { get; set; } = 3;

    /// <summary>
    /// Recent ranked games read per seed. One spreads the sample over more lobbies — the
    /// id list is amortised over fewer games, but one player's games are not independent.
    /// </summary>
    [Range(1, 20)]
    public int MatchesPerSeed { get; set; } = 1;

    /// <summary>
    /// How far back a seed's games are listed. Keeps the sample on the patches the benchmark
    /// read pools, and bounds how long the ledger has to remember a match.
    /// </summary>
    [Range(typeof(TimeSpan), "1.00:00:00", "60.00:00:00")]
    public TimeSpan MatchLookback { get; set; } = TimeSpan.FromDays(14);
}
