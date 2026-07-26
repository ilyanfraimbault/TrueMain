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
}
