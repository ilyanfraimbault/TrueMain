namespace Ingestor.Options;

/// <summary>
/// Options for the ManualSeedProcess (the "seed by Riot ID" intake, #409).
/// <see cref="TopChampionsPerAccount"/> and <see cref="MaxLastPlayDays"/> shape
/// the mastery-derived main candidates exactly like Discovery does; their
/// defaults mirror <see cref="DiscoveryOptions"/> so a seeded account gets the
/// same candidate set a ladder-discovered one would.
/// </summary>
public class ManualSeedOptions
{
    public const string SectionName = "ManualSeed";

    /// <summary>Maximum number of Pending seed requests claimed per run.</summary>
    public int BatchSize { get; set; } = 25;

    /// <summary>Top-N champions (by mastery points) considered as main candidates.</summary>
    public int TopChampionsPerAccount { get; set; } = 10;

    /// <summary>
    /// Skip candidates last played more than this many days ago. 0 disables the
    /// recency filter — useful for a manual seed where an operator may want the
    /// account in regardless of how stale its mastery is.
    /// </summary>
    public int MaxLastPlayDays { get; set; }
}
