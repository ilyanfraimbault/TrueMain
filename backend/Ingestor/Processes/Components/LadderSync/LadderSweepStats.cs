using Ingestor.Processes.Summaries;

namespace Ingestor.Processes.Components.LadderSync;

/// <summary>
/// The counters one ladder-sync run accumulates, and their reduction to the persisted summary.
/// </summary>
internal sealed class LadderSweepStats
{
    private readonly Dictionary<string, int> _entriesByTier = new(StringComparer.Ordinal);

    public int ApexCalls { get; set; }
    public int PagedCalls { get; set; }
    public int FailedCalls { get; set; }
    public int EntriesFetched { get; private set; }
    public int AccountsMatched { get; set; }
    public int RankInserted { get; set; }
    public int RankUpdated { get; set; }
    public int RankUnchanged { get; set; }

    public void Count(string tier)
    {
        EntriesFetched++;
        _entriesByTier[tier] = _entriesByTier.GetValueOrDefault(tier) + 1;
    }

    public LadderSyncSummary ToSummary()
    {
        // Per-tier entry counts are what make the sweep depth a measurable decision rather
        // than a guess: a tier whose entries barely intersect our account pool is paying full
        // page cost for nothing and should be dropped from the scope.
        var tiers = _entriesByTier
            .OrderByDescending(pair => pair.Value)
            .Select(pair => new LadderSyncTierSummary(pair.Key, pair.Value))
            .ToList();

        return new LadderSyncSummary(
            ApexCalls,
            PagedCalls,
            FailedCalls,
            EntriesFetched,
            AccountsMatched,
            RankInserted,
            RankUpdated,
            RankUnchanged,
            tiers);
    }
}
