namespace Ingestor.Options;

public class CoverageOptions
{
    public const string SectionName = "Coverage";

    /// <summary>
    /// Target number of active mains per champion <em>per region</em> (#1150 made the signal
    /// region-scoped). Drives the shared scarcity signal: champions below this target get a
    /// scoring bonus (A) and a relaxed IsMain threshold (C), regions below it get a larger
    /// share of the claim batch and a larger share of it spent on breadth (#1361).
    ///
    /// <para>
    /// 50, not the 20 this started at (#1531). 20 was set when we held almost no mains and
    /// corresponded to a ~200 games/patch floor at the observed ~13.8 games per active main;
    /// every tracked region has since passed it, one of them by more than an order of
    /// magnitude. A signal every region satisfies has no dynamic range left to say "this
    /// region is thin", which is the one thing it exists to say: the deficit read ~0
    /// everywhere, the claim's adaptive share pinned at its depth extreme, and breadth stopped.
    /// 50 is a product decision — the variety of mains we want behind a champion's stats in
    /// each region — not a tuning knob derived from throughput.
    /// </para>
    /// </summary>
    public int TargetMainsPerChampion { get; set; } = 50;
}
