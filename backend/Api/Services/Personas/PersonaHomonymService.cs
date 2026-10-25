using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;
using TrueMain.Requests.Ops;

namespace TrueMain.Services.Personas;

public interface IPersonaHomonymService
{
    /// <summary>Merges known players the operator recognised as one person into the one kept, in one transaction.</summary>
    Task<PersonaHomonymOutcome<PersonaHomonymMergeResponse>> MergeAsync(PersonaHomonymMergeRequest request, CancellationToken ct);

    /// <summary>Marks every pair among the given known players as different people.</summary>
    Task<PersonaHomonymOutcome<PersonaHomonymDistinctResponse>> MarkDistinctAsync(
        PersonaHomonymDistinctRequest request, CancellationToken ct);
}

public sealed record PersonaHomonymOutcome<T>(PersonaEditResult Result, string? Error = null, T? Response = null)
    where T : class;

/// <summary>
/// The operator's side of the homonyms queue (#2271): same-name known players are either one person
/// — merged with <see cref="PersonaMerger"/>, like a conflict's merge — or different people, which
/// <c>persona_distinct_pairs</c> remembers so the queue does not ask again. Both are audited.
/// </summary>
public sealed class PersonaHomonymService(TrueMainDbContext db, PersonaMerger merger, PersonaAudit audit)
    : IPersonaHomonymService
{
    /// <summary>More than any group the queue shows; a guard against a runaway request.</summary>
    public const int MaxPersonas = 20;

    public async Task<PersonaHomonymOutcome<PersonaHomonymMergeResponse>> MergeAsync(
        PersonaHomonymMergeRequest request, CancellationToken ct)
    {
        var mergeIds = (request.MergeIds ?? []).Distinct().ToList();
        if (request.KeepId is not { } keepId || mergeIds.Count is 0 or > MaxPersonas || mergeIds.Contains(keepId))
        {
            return new(PersonaEditResult.Invalid, $"keepId and 1-{MaxPersonas} mergeIds other than it are required.");
        }

        var strategy = db.Database.CreateExecutionStrategy();
        var outcome = await strategy.ExecuteAsync(
            async token =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(token);
                var slugs = await LiveSlugsAsync([keepId, .. mergeIds], token);
                if (slugs is null)
                {
                    return NotLive<PersonaHomonymMergeResponse>();
                }

                var alsoResolved = 0;
                foreach (var mergedId in mergeIds)
                {
                    alsoResolved += await merger.MergeAsync(keepId, mergedId, null, token) ?? 0;
                }

                await db.SaveChangesAsync(token);
                await tx.CommitAsync(token);
                return new PersonaHomonymOutcome<PersonaHomonymMergeResponse>(
                    PersonaEditResult.Ok,
                    Response: new PersonaHomonymMergeResponse
                    {
                        KeptSlug = slugs[keepId],
                        MergedSlugs = mergeIds.Select(id => slugs[id]).ToList(),
                        AlsoResolved = alsoResolved,
                    });
            },
            ct);

        if (outcome.Response is { } merged)
        {
            await audit.RecordAsync(
                "merge_personas", keepId, merged.KeptSlug, new() { ["merged"] = string.Join(",", merged.MergedSlugs) }, ct);
        }

        return outcome;
    }

    public async Task<PersonaHomonymOutcome<PersonaHomonymDistinctResponse>> MarkDistinctAsync(
        PersonaHomonymDistinctRequest request, CancellationToken ct)
    {
        var ids = (request.PersonaIds ?? []).Distinct().ToList();
        if (ids.Count is < 2 or > MaxPersonas)
        {
            return new(PersonaEditResult.Invalid, $"personaIds must hold 2-{MaxPersonas} distinct known players.");
        }

        if (await LiveSlugsAsync(ids, ct) is not { } slugs)
        {
            return NotLive<PersonaHomonymDistinctResponse>();
        }

        var existing = await db.PersonaDistinctPairs
            .AsNoTracking()
            .Where(p => ids.Contains(p.PersonaId) && ids.Contains(p.OtherPersonaId))
            .Select(p => new { p.PersonaId, p.OtherPersonaId })
            .ToListAsync(ct);
        var marked = 0;
        for (var i = 0; i < ids.Count; i++)
        {
            for (var j = i + 1; j < ids.Count; j++)
            {
                var (low, high) = PersonaHomonyms.Ordered(ids[i], ids[j]);
                if (!existing.Any(p => p.PersonaId == low && p.OtherPersonaId == high))
                {
                    db.PersonaDistinctPairs.Add(new PersonaDistinctPair { PersonaId = low, OtherPersonaId = high });
                    marked++;
                }
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent request marked one of these pairs first: the decision is the same.
            db.ChangeTracker.Clear();
            return new(PersonaEditResult.Conflict, "These known players changed meanwhile; reload them.");
        }

        await audit.RecordAsync(
            "mark_personas_distinct", ids[0], slugs[ids[0]], new() { ["others"] = string.Join(",", ids.Skip(1).Select(id => slugs[id])) }, ct);
        return new(PersonaEditResult.Ok, Response: new PersonaHomonymDistinctResponse { Marked = marked });
    }

    /// <summary>The slug of each persona, or null unless every one is live.</summary>
    private async Task<Dictionary<Guid, string>?> LiveSlugsAsync(List<Guid> ids, CancellationToken ct)
    {
        var live = await db.Personas
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.MergedIntoPersonaId == null && p.RemovedAtUtc == null)
            .Select(p => new { p.Id, p.Slug })
            .ToDictionaryAsync(p => p.Id, p => p.Slug, ct);
        return live.Count == ids.Count ? live : null;
    }

    private static PersonaHomonymOutcome<T> NotLive<T>()
        where T : class =>
        new(PersonaEditResult.Conflict, "A known player is unknown, or was merged or removed meanwhile; reload the group.");
}
