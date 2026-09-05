namespace Ingestor.Options;

/// <summary>
/// Options for the champion-mastery activity check (#900), which retires mains that
/// stopped playing. It is the cheap counterpart of match ingestion: one
/// champion-mastery-v4 call per account answers "does this player still play their
/// main?", where the answer used to require pulling a full match-v5 page that came
/// back empty.
/// </summary>
public class MainActivityOptions
{
    public const string SectionName = "MainActivity";

    /// <summary>Accounts checked per run. Each one costs exactly one Riot call.</summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>
    /// A main champion whose mastery <c>lastPlayTime</c> is older than this is deactivated:
    /// it leaves the leaderboard, the coverage counts and the match-ingestion queue, without
    /// its <c>main_champion_stats</c> row being deleted. Set generously enough that a normal
    /// break does not retire a real main — the row comes back on its own, but only after the
    /// player's next check.
    /// </summary>
    public int InactiveAfterDays { get; set; } = 30;

    /// <summary>
    /// Minimum delay between two mastery checks of the same account. <c>0</c> re-checks every
    /// account on every run, which only makes sense with a small pool.
    /// </summary>
    public int RecheckAfterHours { get; set; } = 24;

    /// <summary>
    /// Minimum interval between two activity passes (#1474), with the same contract as
    /// <see cref="DiscoveryOptions.MinRunInterval"/>: measured from the last run that actually
    /// did its work, and <see cref="TimeSpan.Zero"/> (default) runs it every iteration.
    /// <para>
    /// <see cref="RecheckAfterHours"/> throttles how often one <em>account</em> is re-checked,
    /// not how often the process runs: with a pool far larger than <see cref="BatchSize"/>
    /// times the iterations in a day, every account is always due and the process spends a
    /// batch of single-host calls on every iteration. This is the knob that bounds its share
    /// of the fetch lane.
    /// </para>
    /// </summary>
    public TimeSpan MinRunInterval { get; set; } = TimeSpan.Zero;
}
