namespace Ingestor.Processes.Components.MatchIngestion;

/// <summary>
/// What one <see cref="MatchIngestionProcess"/> run may claim, when that is narrower than its
/// configuration (#1457). Scoped: a permanent match-fetch worker narrows it in the DI scope it
/// resolves the process from, so the run claims, and reaps, its routing value's platforms
/// alone. Left unrestricted, the run is the lane step it always was — every configured
/// platform, <c>MatchIngestion:BatchSize</c> accounts.
/// </summary>
public sealed class MatchIngestionScope
{
    /// <summary>The platforms the run is limited to, or null for every configured one.</summary>
    public IReadOnlyCollection<string>? Platforms { get; private set; }

    /// <summary>The accounts the run claims, or null for <c>MatchIngestion:BatchSize</c>.</summary>
    public int? BatchSize { get; private set; }

    /// <summary>Limits the run to <paramref name="platforms"/> and <paramref name="batchSize"/> accounts.</summary>
    public void Restrict(IReadOnlyCollection<string> platforms, int batchSize)
    {
        ArgumentNullException.ThrowIfNull(platforms);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        Platforms = platforms;
        BatchSize = batchSize;
    }
}
