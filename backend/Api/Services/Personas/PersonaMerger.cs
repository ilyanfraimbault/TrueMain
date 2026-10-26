using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Personas;

/// <summary>
/// Folds one persona into another (#2253, #2271): the merged persona's accounts, links, exclusions
/// and different-people marks move to the kept one, and it stays behind empty, pointing at the kept
/// persona — its slug redirects there and the intake feeds the kept persona when a source sends it.
/// Shared by the conflict queue, the automatic same-name merge and the homonyms queue. Runs inside
/// the caller's transaction and leaves the conflict rows it re-points tracked for the caller's save.
/// </summary>
public sealed class PersonaMerger(TrueMainDbContext db)
{
    /// <summary>
    /// Merges <paramref name="mergedId"/> into <paramref name="keptId"/>. Every other open conflict
    /// between the two is settled by it — <paramref name="settlingConflictId"/>, the one the operator
    /// acted on, is the caller's to close. Returns how many it settled, or null when either persona
    /// is no longer live (merged or removed meanwhile).
    /// </summary>
    public async Task<int?> MergeAsync(Guid keptId, Guid mergedId, Guid? settlingConflictId, CancellationToken ct)
    {
        var live = await db.Personas.CountAsync(
            p => (p.Id == keptId || p.Id == mergedId) && p.MergedIntoPersonaId == null && p.RemovedAtUtc == null, ct);
        if (keptId == mergedId || live != 2)
        {
            return null;
        }

        // Riot IDs and tracked accounts are unique across personas, so the accounts move as they are.
        await db.PersonaAccounts.Where(a => a.PersonaId == mergedId)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.PersonaId, keptId), ct);

        // One channel spelled two ways by two sources (a scheme, a case, an escape) is one link (#2286).
        var keptChannels = (await db.PersonaLinks.Where(l => l.PersonaId == keptId).Select(l => l.Url).ToListAsync(ct))
            .Select(PersonaConflictConfidence.ChannelKey)
            .ToHashSet(StringComparer.Ordinal);
        var duplicates = (await db.PersonaLinks.Where(l => l.PersonaId == mergedId).Select(l => new { l.Id, l.Url }).ToListAsync(ct))
            .Where(l => keptChannels.Contains(PersonaConflictConfidence.ChannelKey(l.Url)))
            .Select(l => l.Id)
            .ToList();
        await db.PersonaLinks.Where(l => duplicates.Contains(l.Id)).ExecuteDeleteAsync(ct);
        await db.PersonaLinks.Where(l => l.PersonaId == mergedId)
            .ExecuteUpdateAsync(set => set.SetProperty(l => l.PersonaId, keptId), ct);

        await MoveExclusionsAsync(mergedId, keptId, ct);
        await MoveDistinctPairsAsync(mergedId, keptId, ct);
        var settled = await MoveConflictsAsync(settlingConflictId, mergedId, keptId, ct);

        var now = DateTime.UtcNow;
        await db.Personas.Where(p => p.MergedIntoPersonaId == mergedId)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.MergedIntoPersonaId, keptId), ct);
        await db.Personas.Where(p => p.Id == mergedId).ExecuteUpdateAsync(
            set => set.SetProperty(p => p.MergedIntoPersonaId, keptId).SetProperty(p => p.UpdatedAtUtc, now), ct);
        await db.Personas.Where(p => p.Id == keptId).ExecuteUpdateAsync(set => set.SetProperty(p => p.UpdatedAtUtc, now), ct);
        return settled;
    }

    private async Task MoveExclusionsAsync(Guid fromId, Guid toId, CancellationToken ct)
    {
        var moving = await db.PersonaAccountExclusions.Where(e => e.PersonaId == fromId).ToListAsync(ct);
        var kept = await db.PersonaAccountExclusions
            .AsNoTracking()
            .Where(e => e.PersonaId == toId)
            .Select(e => new { e.PlatformId, e.GameName, e.TagLine })
            .ToListAsync(ct);
        var keys = kept.Select(e => PersonaExclusions.Key(e.PlatformId, e.GameName, e.TagLine)).ToHashSet(StringComparer.Ordinal);
        foreach (var exclusion in moving)
        {
            if (keys.Add(PersonaExclusions.Key(exclusion.PlatformId, exclusion.GameName, exclusion.TagLine)))
            {
                exclusion.PersonaId = toId;
            }
            else
            {
                db.PersonaAccountExclusions.Remove(exclusion);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// What the operator said of the merged persona holds for the kept one: different people from
    /// the merged persona are different from the kept persona too. A mark between the two goes.
    /// </summary>
    private async Task MoveDistinctPairsAsync(Guid fromId, Guid toId, CancellationToken ct)
    {
        var others = await db.PersonaDistinctPairs
            .AsNoTracking()
            .Where(p => p.PersonaId == fromId || p.OtherPersonaId == fromId)
            .Select(p => p.PersonaId == fromId ? p.OtherPersonaId : p.PersonaId)
            .ToListAsync(ct);
        await db.PersonaDistinctPairs.Where(p => p.PersonaId == fromId || p.OtherPersonaId == fromId).ExecuteDeleteAsync(ct);

        var pairs = others.Where(other => other != toId).Select(other => PersonaHomonyms.Ordered(toId, other)).ToList();
        if (pairs.Count == 0)
        {
            return;
        }

        var existing = await db.PersonaDistinctPairs
            .AsNoTracking()
            .Where(p => p.PersonaId == toId || p.OtherPersonaId == toId)
            .Select(p => new { p.PersonaId, p.OtherPersonaId })
            .ToListAsync(ct);
        foreach (var (low, high) in pairs.Distinct())
        {
            if (!existing.Any(p => p.PersonaId == low && p.OtherPersonaId == high))
            {
                db.PersonaDistinctPairs.Add(new PersonaDistinctPair { PersonaId = low, OtherPersonaId = high });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Every open conflict between the two is settled by the merge, and the merged persona's other
    /// open claims and holdings become the kept persona's. Resolved ones keep their history.
    /// </summary>
    private async Task<int> MoveConflictsAsync(Guid? settlingConflictId, Guid mergedId, Guid keptId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var between = await db.PersonaAccountConflicts
            .Where(c => c.Id != settlingConflictId && c.Status == PersonaConflictStatus.Open
                        && (c.ClaimantPersonaId == mergedId || c.ClaimantPersonaId == keptId)
                        && (c.OwnerPersonaId == mergedId || c.OwnerPersonaId == keptId))
            .ExecuteUpdateAsync(
                set => set.SetProperty(c => c.Status, PersonaConflictStatus.Resolved)
                    .SetProperty(c => c.Resolution, PersonaConflictResolution.Merged)
                    .SetProperty(c => c.ResolvedAtUtc, now),
                ct);

        await db.PersonaAccountConflicts
            .Where(c => c.Status == PersonaConflictStatus.Open && c.OwnerPersonaId == mergedId)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.OwnerPersonaId, keptId), ct);

        var claims = await db.PersonaAccountConflicts
            .Where(c => c.Id != settlingConflictId && c.Status == PersonaConflictStatus.Open && c.ClaimantPersonaId == mergedId)
            .ToListAsync(ct);
        foreach (var claim in claims)
        {
            var nameLower = claim.GameName.ToLowerInvariant();
            var tagLower = claim.TagLine.ToLowerInvariant();
            var duplicate = await db.PersonaAccountConflicts.AnyAsync(
                c => c.ClaimantPersonaId == keptId && c.PlatformId == claim.PlatformId
                     && c.GameName.ToLower() == nameLower && c.TagLine.ToLower() == tagLower,
                ct);
            if (duplicate)
            {
                db.PersonaAccountConflicts.Remove(claim);
            }
            else
            {
                claim.ClaimantPersonaId = keptId;
            }
        }

        return between;
    }
}
