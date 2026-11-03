using Data;
using Data.Entities;
using Data.Logging.Mongo;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;
using TrueMain.Requests.Ops;

namespace TrueMain.Services.Personas;

public interface IPersonaConflictResolutionService
{
    /// <summary>Settles one open conflict with the operator's action, in one transaction, and audits it.</summary>
    Task<PersonaConflictResolveOutcome> ResolveAsync(Guid id, PersonaConflictResolveRequest request, CancellationToken ct);

    /// <summary>The same, audited under <paramref name="actor"/>: <c>auto</c> for the same-name merge (#2271).</summary>
    Task<PersonaConflictResolveOutcome> ResolveAsync(
        Guid id, PersonaConflictResolveRequest request, string actor, CancellationToken ct);
}

public enum PersonaConflictResolveResult
{
    Ok,
    NotFound,
    Invalid,

    /// <summary>Already resolved, or the account changed hands since: the operator should reload.</summary>
    NotOpen,
}

public sealed record PersonaConflictResolveOutcome(
    PersonaConflictResolveResult Result, string? Error = null, PersonaConflictResolvedResponse? Resolved = null);

/// <summary>
/// The operator's side of the persona conflict queue (#2253). Every action leaves the two unique
/// rules of <c>persona_accounts</c> intact (one persona per tracked account, one per Riot ID) and
/// writes what the scrapers must not undo: an exclusion for the persona that lost the account, or,
/// for a merge, the merged persona's pointer to the kept one.
/// </summary>
public sealed class PersonaConflictResolutionService(
    TrueMainDbContext db,
    PersonaMerger merger,
    IAuditLog auditLog,
    ILogger<PersonaConflictResolutionService> logger) : IPersonaConflictResolutionService
{
    public Task<PersonaConflictResolveOutcome> ResolveAsync(
        Guid id, PersonaConflictResolveRequest request, CancellationToken ct) =>
        ResolveAsync(id, request, AuditActors.Operator, ct);

    public async Task<PersonaConflictResolveOutcome> ResolveAsync(
        Guid id, PersonaConflictResolveRequest request, string actor, CancellationToken ct)
    {
        if (Parse(request) is not { } action)
        {
            return new PersonaConflictResolveOutcome(
                PersonaConflictResolveResult.Invalid,
                "action must be move, keep, detach or merge; a merge also names keep: claimant or owner.");
        }

        // The API runs a retrying execution strategy: an explicit transaction must be one replayable unit.
        var strategy = db.Database.CreateExecutionStrategy();
        var outcome = await strategy.ExecuteAsync(
            async token =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(token);
                var conflict = await db.PersonaAccountConflicts.FirstOrDefaultAsync(c => c.Id == id, token);
                if (conflict is null)
                {
                    return new PersonaConflictResolveOutcome(PersonaConflictResolveResult.NotFound);
                }

                if (conflict.Status != PersonaConflictStatus.Open)
                {
                    return new PersonaConflictResolveOutcome(PersonaConflictResolveResult.NotOpen, "This conflict is already resolved.");
                }

                var result = action.Resolution switch
                {
                    PersonaConflictResolution.Moved => await MoveAsync(conflict, token),
                    PersonaConflictResolution.Kept => await KeepAsync(conflict, token),
                    PersonaConflictResolution.Detached => await DetachAsync(conflict, token),
                    _ => await MergeAsync(conflict, action.KeepClaimant, token),
                };
                if (result.Result != PersonaConflictResolveResult.Ok)
                {
                    return result;
                }

                await db.SaveChangesAsync(token);
                await tx.CommitAsync(token);
                return result;
            },
            ct);

        if (outcome.Resolved is { } resolved)
        {
            await AuditAsync(id, resolved, actor, ct);
        }

        return outcome;
    }

    private static (PersonaConflictResolution Resolution, bool KeepClaimant)? Parse(PersonaConflictResolveRequest request)
    {
        var keep = request.Keep?.Trim().ToLowerInvariant();
        return request.Action?.Trim().ToLowerInvariant() switch
        {
            "move" => (PersonaConflictResolution.Moved, false),
            "keep" => (PersonaConflictResolution.Kept, false),
            "detach" => (PersonaConflictResolution.Detached, false),
            "merge" when keep is "claimant" or "owner" => (PersonaConflictResolution.Merged, keep == "claimant"),
            _ => null,
        };
    }

    /// <summary>The account goes to the claimant; the former holder may not claim it back.</summary>
    private async Task<PersonaConflictResolveOutcome> MoveAsync(PersonaAccountConflict conflict, CancellationToken ct)
    {
        var ownerRow = await HeldRowAsync(conflict.OwnerPersonaId, conflict, ct);
        if (ownerRow is null && await HeldByAnotherAsync(conflict, ct))
        {
            return Stale();
        }

        var claimantRow = await HeldRowAsync(conflict.ClaimantPersonaId, conflict, ct);
        var accountId = ownerRow?.RiotAccountId ?? conflict.RiotAccountId;
        if (ownerRow is not null)
        {
            // Out first: both unique indexes of persona_accounts would refuse the claimant's row otherwise.
            db.PersonaAccounts.Remove(ownerRow);
            await db.SaveChangesAsync(ct);
        }

        var now = DateTime.UtcNow;
        if (claimantRow is not null)
        {
            claimantRow.RiotAccountId = accountId ?? claimantRow.RiotAccountId;
            claimantRow.ResolvedAtUtc = claimantRow.RiotAccountId is null ? null : claimantRow.ResolvedAtUtc ?? now;
            claimantRow.Source = PersonaSource.Manual;
        }
        else
        {
            db.PersonaAccounts.Add(new PersonaAccount
            {
                PersonaId = conflict.ClaimantPersonaId,
                GameName = conflict.GameName,
                TagLine = conflict.TagLine,
                PlatformId = conflict.PlatformId,
                RiotAccountId = accountId,
                ResolvedAtUtc = accountId is null ? null : now,
                Source = PersonaSource.Manual,
            });
        }

        await ExcludeAsync(conflict.OwnerPersonaId, conflict, ownerRow, accountId, ct);
        Close(conflict, PersonaConflictResolution.Moved);
        var alsoResolved = await HandOverOpenConflictsAsync(conflict, accountId, ct);
        return Ok(conflict, PersonaConflictResolution.Moved, await SlugAsync(conflict.ClaimantPersonaId, ct), null, alsoResolved);
    }

    /// <summary>The account stays with its holder; the claimant may not claim it again.</summary>
    private async Task<PersonaConflictResolveOutcome> KeepAsync(PersonaAccountConflict conflict, CancellationToken ct)
    {
        await DropUnresolvedClaimAsync(conflict, ct);
        await ExcludeAsync(conflict.ClaimantPersonaId, conflict, null, conflict.RiotAccountId, ct);
        Close(conflict, PersonaConflictResolution.Kept);
        return Ok(conflict, PersonaConflictResolution.Kept, await SlugAsync(conflict.OwnerPersonaId, ct), null, 0);
    }

    /// <summary>The account belongs to neither: off the holder, barred for both.</summary>
    private async Task<PersonaConflictResolveOutcome> DetachAsync(PersonaAccountConflict conflict, CancellationToken ct)
    {
        var ownerRow = await HeldRowAsync(conflict.OwnerPersonaId, conflict, ct);
        var accountId = ownerRow?.RiotAccountId ?? conflict.RiotAccountId;
        if (ownerRow is not null)
        {
            db.PersonaAccounts.Remove(ownerRow);
        }

        await DropUnresolvedClaimAsync(conflict, ct);
        await ExcludeAsync(conflict.OwnerPersonaId, conflict, ownerRow, accountId, ct);
        await ExcludeAsync(conflict.ClaimantPersonaId, conflict, null, accountId, ct);
        Close(conflict, PersonaConflictResolution.Detached);
        return Ok(conflict, PersonaConflictResolution.Detached, null, null, 0);
    }

    /// <summary>The two personas are one person: merged into the one the operator kept (<see cref="PersonaMerger"/>).</summary>
    private async Task<PersonaConflictResolveOutcome> MergeAsync(
        PersonaAccountConflict conflict, bool keepClaimant, CancellationToken ct)
    {
        var keptId = keepClaimant ? conflict.ClaimantPersonaId : conflict.OwnerPersonaId;
        var mergedId = keepClaimant ? conflict.OwnerPersonaId : conflict.ClaimantPersonaId;
        if (await merger.MergeAsync(keptId, mergedId, conflict.Id, ct) is not { } alsoResolved)
        {
            return Stale();
        }

        Close(conflict, PersonaConflictResolution.Merged);
        return Ok(conflict, PersonaConflictResolution.Merged, await SlugAsync(keptId, ct), await SlugAsync(mergedId, ct), alsoResolved);
    }

    /// <summary>
    /// After a move: the account's other open claims now face its new holder; a claim by that new
    /// holder itself is settled by the move.
    /// </summary>
    private async Task<int> HandOverOpenConflictsAsync(PersonaAccountConflict conflict, Guid? accountId, CancellationToken ct)
    {
        var nameLower = conflict.GameName.ToLowerInvariant();
        var tagLower = conflict.TagLine.ToLowerInvariant();
        var others = await db.PersonaAccountConflicts
            .Where(c => c.Id != conflict.Id && c.Status == PersonaConflictStatus.Open
                        && ((accountId != null && c.RiotAccountId == accountId)
                            || (c.PlatformId == conflict.PlatformId
                                && c.GameName.ToLower() == nameLower && c.TagLine.ToLower() == tagLower)))
            .ToListAsync(ct);
        var settled = 0;
        foreach (var other in others)
        {
            other.OwnerPersonaId = conflict.ClaimantPersonaId;
            if (other.ClaimantPersonaId == conflict.ClaimantPersonaId)
            {
                Close(other, PersonaConflictResolution.Moved);
                settled++;
            }
        }

        return settled;
    }

    /// <summary>The persona's row for the disputed account: by tracked account first, else by Riot ID.</summary>
    private Task<PersonaAccount?> HeldRowAsync(Guid personaId, PersonaAccountConflict conflict, CancellationToken ct)
    {
        var accountId = conflict.RiotAccountId;
        var nameLower = conflict.GameName.ToLowerInvariant();
        var tagLower = conflict.TagLine.ToLowerInvariant();
        return db.PersonaAccounts
            .Where(a => a.PersonaId == personaId
                        && ((accountId != null && a.RiotAccountId == accountId)
                            || (a.PlatformId == conflict.PlatformId
                                && a.GameName.ToLower() == nameLower && a.TagLine.ToLower() == tagLower)))
            .OrderBy(a => a.RiotAccountId == null)
            .FirstOrDefaultAsync(ct);
    }

    private Task<bool> HeldByAnotherAsync(PersonaAccountConflict conflict, CancellationToken ct)
    {
        var accountId = conflict.RiotAccountId;
        var nameLower = conflict.GameName.ToLowerInvariant();
        var tagLower = conflict.TagLine.ToLowerInvariant();
        return db.PersonaAccounts.AnyAsync(
            a => a.PersonaId != conflict.ClaimantPersonaId
                 && ((accountId != null && a.RiotAccountId == accountId)
                     || (a.PlatformId == conflict.PlatformId
                         && a.GameName.ToLower() == nameLower && a.TagLine.ToLower() == tagLower)),
            ct);
    }

    /// <summary>A claim the claimant stored before the account was found elsewhere (a rename) goes with it.</summary>
    private async Task DropUnresolvedClaimAsync(PersonaAccountConflict conflict, CancellationToken ct)
    {
        if (await HeldRowAsync(conflict.ClaimantPersonaId, conflict, ct) is { RiotAccountId: null } claimantRow)
        {
            db.PersonaAccounts.Remove(claimantRow);
        }
    }

    /// <summary>
    /// Bars the persona from the disputed account under every spelling it may come back with: the
    /// conflict's Riot ID, the persona's own row's (the spelling its source sends) and the tracked account.
    /// </summary>
    private async Task ExcludeAsync(
        Guid personaId, PersonaAccountConflict conflict, PersonaAccount? personaRow, Guid? accountId, CancellationToken ct)
    {
        var spellings = new List<(string GameName, string TagLine)> { (conflict.GameName, conflict.TagLine) };
        if (personaRow is not null
            && (!string.Equals(personaRow.GameName, conflict.GameName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(personaRow.TagLine, conflict.TagLine, StringComparison.OrdinalIgnoreCase)))
        {
            spellings.Add((personaRow.GameName, personaRow.TagLine));
        }

        foreach (var (gameName, tagLine) in spellings)
        {
            if (!await HasExclusionAsync(personaId, conflict.PlatformId, gameName, tagLine, ct))
            {
                db.PersonaAccountExclusions.Add(new PersonaAccountExclusion
                {
                    PersonaId = personaId,
                    GameName = gameName,
                    TagLine = tagLine,
                    PlatformId = conflict.PlatformId,
                    RiotAccountId = accountId,
                    ConflictId = conflict.Id,
                });
            }
        }
    }

    private async Task<bool> HasExclusionAsync(Guid personaId, string platformId, string gameName, string tagLine, CancellationToken ct)
    {
        if (db.PersonaAccountExclusions.Local.Any(e =>
                e.PersonaId == personaId && e.PlatformId == platformId
                && string.Equals(e.GameName, gameName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.TagLine, tagLine, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var nameLower = gameName.ToLowerInvariant();
        var tagLower = tagLine.ToLowerInvariant();
        return await db.PersonaAccountExclusions.AnyAsync(
            e => e.PersonaId == personaId && e.PlatformId == platformId
                 && e.GameName.ToLower() == nameLower && e.TagLine.ToLower() == tagLower,
            ct);
    }

    private static void Close(PersonaAccountConflict conflict, PersonaConflictResolution resolution)
    {
        conflict.Status = PersonaConflictStatus.Resolved;
        conflict.Resolution = resolution;
        conflict.ResolvedAtUtc = DateTime.UtcNow;
    }

    private Task<string?> SlugAsync(Guid personaId, CancellationToken ct) =>
        db.Personas.AsNoTracking().Where(p => p.Id == personaId).Select(p => p.Slug).FirstOrDefaultAsync(ct);

    private static PersonaConflictResolveOutcome Stale() => new(
        PersonaConflictResolveResult.NotOpen,
        "The account or one of the personas changed since this conflict was raised; reload it.");

    private static PersonaConflictResolveOutcome Ok(
        PersonaAccountConflict conflict, PersonaConflictResolution resolution, string? holderSlug, string? mergedSlug, int alsoResolved) =>
        new(PersonaConflictResolveResult.Ok, Resolved: new PersonaConflictResolvedResponse
        {
            Id = conflict.Id,
            Resolution = resolution.ToString(),
            HolderSlug = holderSlug,
            MergedSlug = mergedSlug,
            AlsoResolved = alsoResolved,
        });

    private async Task AuditAsync(Guid id, PersonaConflictResolvedResponse resolved, string actor, CancellationToken ct)
    {
        // Best effort, like the seed intake's: the resolution is committed; a Mongo outage must not 500 it.
        try
        {
            var metadata = new Dictionary<string, string> { ["resolution"] = resolved.Resolution };
            if (resolved.HolderSlug is { } holder)
            {
                metadata["holder"] = holder;
            }

            if (resolved.MergedSlug is { } merged)
            {
                metadata["merged"] = merged;
            }

            await auditLog.RecordAsync(
                action: AuditActions.ResolvePersonaConflict,
                actor: actor,
                targetType: AuditTargets.PersonaAccountConflict,
                targetId: id.ToString(),
                metadata: metadata,
                ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Audit write failed for persona conflict {ConflictId}; the resolution was committed.", id);
        }
    }
}
