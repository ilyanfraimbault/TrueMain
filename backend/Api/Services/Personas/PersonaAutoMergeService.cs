using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;
using TrueMain.Requests.Ops;

namespace TrueMain.Services.Personas;

public interface IPersonaAutoMergeService
{
    /// <summary>
    /// Settles as merges the open conflicts whose two personas are homonyms, oldest claim first, up
    /// to <see cref="PersonaAutoMergeService.SweepLimit"/> per call.
    /// </summary>
    Task<PersonaAutoMergeResponse> MergeHomonymConflictsAsync(CancellationToken ct);
}

/// <summary>
/// The rule the operator gave the conflict queue (#2271): two known players with the same name
/// (<see cref="PersonaHomonyms"/>) that claim the same Riot account are one person, published twice
/// by two sources under two slugs. Such a conflict needs no decision, so it is merged without one —
/// into the older persona, whose slug the site has advertised the longest — through the conflict
/// resolution itself, audited as <c>auto</c>. The intake sweeps after every batch; the admin can
/// sweep the backlog on demand.
/// </summary>
public sealed class PersonaAutoMergeService(
    TrueMainDbContext db,
    IPersonaConflictResolutionService resolution,
    ILogger<PersonaAutoMergeService> logger) : IPersonaAutoMergeService
{
    /// <summary>A sweep's bound: a merge is a handful of statements, and the intake waits on it.</summary>
    public const int SweepLimit = 500;

    public async Task<PersonaAutoMergeResponse> MergeHomonymConflictsAsync(CancellationToken ct)
    {
        var refused = new List<Guid>();
        var merged = 0;
        var alsoResolved = 0;
        while (merged + refused.Count < SweepLimit && await NextAsync(refused, ct) is { } next)
        {
            var outcome = await resolution.ResolveAsync(
                next.Id,
                new PersonaConflictResolveRequest { Action = "merge", Keep = next.KeepClaimant ? "claimant" : "owner" },
                "auto",
                ct);
            if (outcome.Resolved is { } resolved)
            {
                merged++;
                alsoResolved += resolved.AlsoResolved;
            }
            else
            {
                // Changed under the sweep (a concurrent operator action): left for the queue.
                refused.Add(next.Id);
            }
        }

        if (merged > 0)
        {
            logger.LogInformation(
                "Merged {Merged} same-name persona conflict(s) automatically ({AlsoResolved} more settled by them).",
                merged,
                alsoResolved);
        }

        return new PersonaAutoMergeResponse { Merged = merged, AlsoResolved = alsoResolved, Skipped = refused.Count };
    }

    private async Task<Candidate?> NextAsync(List<Guid> refused, CancellationToken ct)
    {
        var sql = $$"""
            SELECT c."Id", (cl."CreatedAtUtc", cl."Id") <= (o."CreatedAtUtc", o."Id") AS "KeepClaimant"
            FROM persona_account_conflicts c
            JOIN personas cl ON cl."Id" = c."ClaimantPersonaId"
            JOIN personas o ON o."Id" = c."OwnerPersonaId"
            WHERE c."Status" = 'Open'
              AND cl."MergedIntoPersonaId" IS NULL AND cl."RemovedAtUtc" IS NULL
              AND o."MergedIntoPersonaId" IS NULL AND o."RemovedAtUtc" IS NULL
              AND {{PersonaHomonyms.NameKeySql("cl")}} = {{PersonaHomonyms.NameKeySql("o")}}
              AND {{PersonaHomonyms.NameKeySql("cl")}} <> ''
              AND NOT (c."Id" = ANY({0}))
            ORDER BY c."FirstSeenAtUtc", c."Id"
            LIMIT 1
            """;
        db.ChangeTracker.Clear();
        return await db.Database.SqlQueryRaw<Candidate>(sql, refused.ToArray()).FirstOrDefaultAsync(ct);
    }

    private sealed record Candidate(Guid Id, bool KeepClaimant);
}
