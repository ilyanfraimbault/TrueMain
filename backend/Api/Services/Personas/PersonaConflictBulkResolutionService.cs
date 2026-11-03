using Data;
using Data.Entities;
using Data.Logging.Mongo;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;
using TrueMain.Requests.Ops;

namespace TrueMain.Services.Personas;

public interface IPersonaConflictBulkResolutionService
{
    /// <summary>
    /// Merges each previewed conflict that still qualifies into its older known player, one at a
    /// time through the single resolution (so each is audited like one), and reports every id.
    /// </summary>
    Task<PersonaConflictBulkOutcome> ResolveAsync(PersonaConflictBulkResolveRequest request, CancellationToken ct);
}

public sealed record PersonaConflictBulkOutcome(string? Error, PersonaConflictBulkResolveResponse? Response);

/// <summary>
/// The bulk side of the conflict queue (#2295). The operator confirms an explicit list; each id is
/// checked again here — still open, still free of a differing channel, still at the floor — because
/// the links may have moved since the preview. A conflict with a differing channel is never merged
/// in bulk, whatever its score.
/// </summary>
public sealed class PersonaConflictBulkResolutionService(
    TrueMainDbContext db,
    IPersonaConflictResolutionService resolution) : IPersonaConflictBulkResolutionService
{
    public async Task<PersonaConflictBulkOutcome> ResolveAsync(PersonaConflictBulkResolveRequest request, CancellationToken ct)
    {
        var ids = (request.Ids ?? []).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new PersonaConflictBulkOutcome("ids must name at least one conflict.", null);
        }

        if (ids.Count > PersonaConflictBulk.BatchLimit)
        {
            return new PersonaConflictBulkOutcome(
                $"ids holds {ids.Count} conflicts; at most {PersonaConflictBulk.BatchLimit} per bulk resolution.", null);
        }

        if (request.MinScore is not { } floor || floor < PersonaConflictBulk.MinimumFloor)
        {
            return new PersonaConflictBulkOutcome(
                $"minScore must be at least {PersonaConflictBulk.MinimumFloor} (one shared social channel).", null);
        }

        var outcomes = new List<PersonaConflictBulkOutcomeReadModel>(ids.Count);
        foreach (var id in ids)
        {
            outcomes.Add(await ResolveOneAsync(id, floor, ct));
        }

        var resolved = outcomes.Count(o => o.Resolution is not null);
        return new PersonaConflictBulkOutcome(null, new PersonaConflictBulkResolveResponse
        {
            Resolved = resolved,
            Skipped = outcomes.Count - resolved,
            Outcomes = outcomes,
        });
    }

    private async Task<PersonaConflictBulkOutcomeReadModel> ResolveOneAsync(Guid id, int floor, CancellationToken ct)
    {
        // Read fresh: an earlier merge of this batch may have settled this one or folded one of its personas.
        var conflict = await db.PersonaAccountConflicts
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new
            {
                c.Status,
                c.ClaimantPersonaId,
                ClaimantCreatedAtUtc = c.Claimant!.CreatedAtUtc,
                c.OwnerPersonaId,
                OwnerCreatedAtUtc = c.Owner!.CreatedAtUtc,
            })
            .FirstOrDefaultAsync(ct);
        if (conflict is null)
        {
            return Skipped(id, "notFound");
        }

        if (conflict.Status != PersonaConflictStatus.Open)
        {
            return Skipped(id, "notOpen");
        }

        var evidence = (await PersonaConflictBulk.EvidenceAsync(
            db, [(id, conflict.ClaimantPersonaId, conflict.OwnerPersonaId)], ct))[id];
        if (evidence.DifferingSocial.Count > 0)
        {
            return Skipped(id, "differingChannel");
        }

        if (!PersonaConflictBulk.IsEligible(evidence, floor))
        {
            return Skipped(id, "belowFloor");
        }

        var keepClaimant = PersonaConflictBulk.KeepClaimant(
            conflict.ClaimantCreatedAtUtc, conflict.ClaimantPersonaId, conflict.OwnerCreatedAtUtc, conflict.OwnerPersonaId);
        var outcome = await resolution.ResolveAsync(
            id,
            new PersonaConflictResolveRequest { Action = "merge", Keep = keepClaimant ? "claimant" : "owner" },
            AuditActors.Operator,
            ct);
        return outcome.Result switch
        {
            PersonaConflictResolveResult.Ok => new PersonaConflictBulkOutcomeReadModel
            {
                Id = id,
                Status = "resolved",
                Resolution = outcome.Resolved,
            },
            PersonaConflictResolveResult.NotFound => Skipped(id, "notFound"),
            _ => Skipped(id, "changed"),
        };
    }

    private static PersonaConflictBulkOutcomeReadModel Skipped(Guid id, string reason) =>
        new() { Id = id, Status = "skipped", Reason = reason };
}
