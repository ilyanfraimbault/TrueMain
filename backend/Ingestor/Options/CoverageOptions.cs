namespace Ingestor.Options;

public class CoverageOptions
{
    public const string SectionName = "Coverage";

    /// <summary>
    /// Target number of mains per champion. Drives the shared scarcity signal:
    /// champions below this target get a scoring bonus (A) and a relaxed IsMain
    /// threshold (C). ~20 mains corresponds to a ~200 games/patch floor at the
    /// observed ~13.8 games per active main.
    /// </summary>
    public int TargetMainsPerChampion { get; set; } = 20;
}
