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

    /// <summary>
    /// Releases every claim there is, regardless of how much of its lease was left.
    /// Called once at startup, before the first pass (#1513).
    /// </summary>
    /// <remarks>
    /// A restart is the one moment when "held" and "orphaned" mean the same thing: the
    /// ingestor runs a single instance per lane, so every claim on the table was taken by
    /// the incarnation that just died and nothing will ever settle it. Waiting out the
    /// remaining lease instead — up to <c>MatchIngestion:ClaimLeaseMinutes</c> — left the
    /// accounts a redeploy interrupted unclaimable for the rest of it, which is precisely
    /// the reclaim hole around every deploy this exists to close. Never call it while a
    /// pass is running: it cannot tell a live claim from a dead one.
    /// </remarks>
    Task<ExpiredClaimRelease> ReleaseOrphanedClaimsAsync(CancellationToken ct);
}

/// <summary>What one reap freed, counted on both sides because they are not the same
/// population: an account carries one candidate row per champion, and an account can hold a
/// stale claim with no candidate left behind it at all.</summary>
public readonly record struct ExpiredClaimRelease(int Candidates, int Accounts)
{
    public bool IsEmpty => Candidates == 0 && Accounts == 0;
}
