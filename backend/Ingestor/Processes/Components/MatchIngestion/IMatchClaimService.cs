using Data.Repositories;

namespace Ingestor.Processes.Components.MatchIngestion;

public interface IMatchClaimService
{
    Task<List<AccountKey>> ClaimAsync(
        IReadOnlyCollection<string> platforms,
        int batchSize,
        double establishedMainShare,
        TimeSpan lease,
        CancellationToken ct);

    /// <summary>
    /// Reaps claims whose <paramref name="lease"/> has run out: candidates go back to
    /// <c>Queued</c> and their accounts back to <c>Idle</c>. Run before
    /// <see cref="ClaimAsync"/> so what it frees is claimable in the same pass.
    /// </summary>
    /// <remarks>
    /// The lease is the only promise the pipeline makes about a claim, and until #1344
    /// nothing kept it: a hard stop left the rows Processing, and because the claim query
    /// only reaches accounts holding an active main or a <c>Queued</c> candidate, an account
    /// whose candidates were all stuck there became permanently invisible to the one
    /// mechanism that would have settled them. Idempotent and cheap once drained — a
    /// steady-state pass matches nothing and writes nothing.
    /// </remarks>
    Task<ExpiredClaimRelease> ReleaseExpiredClaimsAsync(TimeSpan lease, CancellationToken ct);
}

/// <summary>What one reap freed, counted on both sides because they are not the same
/// population: an account carries one candidate row per champion, and an account can hold a
/// stale claim with no candidate left behind it at all.</summary>
public readonly record struct ExpiredClaimRelease(int Candidates, int Accounts)
{
    public bool IsEmpty => Candidates == 0 && Accounts == 0;
}
