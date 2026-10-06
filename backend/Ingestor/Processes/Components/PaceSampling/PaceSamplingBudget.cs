using System.Text.Json;
using Data.Ops.Mongo;
using Ingestor.Options;

namespace Ingestor.Processes.Components.PaceSampling;

/// <summary>
/// The Riot calls the pace sampler (#1912) may still spend this run: the per-run cap, bounded by
/// what the day's earlier runs left of <see cref="PaceSamplingOptions.MaxRequestsPerDay"/>. The
/// day's spend is read back from the run summaries, the same way the ladder sync's is (#1474).
/// </summary>
internal sealed class PaceSamplingBudget
{
    private readonly int _limit;

    private PaceSamplingBudget(int limit, int spentToday)
    {
        _limit = limit;
        SpentToday = spentToday;
    }

    /// <summary>Calls the day's earlier runs recorded.</summary>
    public int SpentToday { get; }

    /// <summary>Calls this run has made.</summary>
    public int Spent { get; private set; }

    /// <summary>Whether <paramref name="calls"/> more still fit in this run.</summary>
    public bool CanSpend(int calls) => Spent + calls <= _limit;

    /// <summary>Charges one call, before it is made: a failed call was still spent.</summary>
    public void Charge() => Spent++;

    public static PaceSamplingBudget Unlimited(int perRun) => new(Math.Max(0, perRun), 0);

    public static async Task<PaceSamplingBudget> ReadAsync(
        IProcessRunStore processRunStore,
        string processName,
        PaceSamplingOptions options,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var perRun = Math.Max(0, options.MaxRequestsPerRun);
        if (options.MaxRequestsPerDay <= 0)
        {
            return Unlimited(perRun);
        }

        var spentToday = 0;
        foreach (var run in await processRunStore.GetRunSummariesAsync([processName], nowUtc.Date, ct))
        {
            if (run.SummaryJson is not null)
            {
                spentToday += ReadRiotCalls(run.SummaryJson);
            }
        }

        return new PaceSamplingBudget(Math.Min(perRun, Math.Max(0, options.MaxRequestsPerDay - spentToday)), spentToday);
    }

    /// <summary>
    /// The <c>riotCalls</c> counter of a persisted summary; a skip or no-work summary has none.
    /// </summary>
    internal static int ReadRiotCalls(string summaryJson)
    {
        try
        {
            using var document = JsonDocument.Parse(summaryJson);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                   && root.TryGetProperty("riotCalls", out var value)
                   && value.ValueKind == JsonValueKind.Number
                   && value.TryGetInt32(out var parsed)
                ? Math.Max(0, parsed)
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}
