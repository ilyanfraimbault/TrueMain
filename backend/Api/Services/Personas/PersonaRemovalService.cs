using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Personas;

public interface IPersonaRemovalService
{
    /// <summary>
    /// Removes a known player: its page goes, and the scrapers can no longer recreate it. With
    /// <paramref name="takedown"/>, its Riot accounts are also barred from every other persona.
    /// </summary>
    Task<PersonaEditOutcome> RemoveAsync(Guid id, bool takedown, CancellationToken ct);
}

/// <summary>
/// Removal and takedown of a known player (#2019). The persona keeps its row and slug — the same
/// reason a merged persona does — but loses every account, link and open conflict, so
/// <c>GET /players/{slug}</c> answers 404, search, directory and leaderboard tags (which all go
/// through accounts) drop it, and the intake answers <c>removed</c> for the slug.
/// </summary>
/// <remarks>
/// A takedown goes one step further for a person who asked not to be listed: every account the
/// persona held gets an exclusion marked <see cref="PersonaAccountExclusion.IsTakedown"/>, which
/// the intake applies to every persona, so a source listing the same person under another slug
/// cannot put those accounts back on the site. A plain removal leaves the accounts free, for a
/// persona that was simply wrong.
/// </remarks>
public sealed class PersonaRemovalService(TrueMainDbContext db, PersonaAudit audit) : IPersonaRemovalService
{
    public async Task<PersonaEditOutcome> RemoveAsync(Guid id, bool takedown, CancellationToken ct)
    {
        var persona = await db.Personas
            .Include(p => p.Accounts)
            .Include(p => p.Links)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (PersonaEditService.NotLive(persona) is { } refused)
        {
            return refused;
        }

        var accounts = persona!.Accounts.ToList();
        if (takedown)
        {
            var existing = await db.PersonaAccountExclusions.Where(e => e.PersonaId == id).ToListAsync(ct);
            foreach (var account in accounts)
            {
                var exclusion = existing.FirstOrDefault(e =>
                    e.PlatformId == account.PlatformId
                    && string.Equals(e.GameName, account.GameName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.TagLine, account.TagLine, StringComparison.OrdinalIgnoreCase));
                if (exclusion is null)
                {
                    db.PersonaAccountExclusions.Add(new PersonaAccountExclusion
                    {
                        PersonaId = id,
                        GameName = account.GameName,
                        TagLine = account.TagLine,
                        PlatformId = account.PlatformId,
                        RiotAccountId = account.RiotAccountId,
                        IsTakedown = true,
                    });
                }
                else
                {
                    exclusion.IsTakedown = true;
                    exclusion.RiotAccountId ??= account.RiotAccountId;
                }
            }
        }

        db.PersonaAccounts.RemoveRange(accounts);
        db.PersonaLinks.RemoveRange(persona.Links);

        // Its claims are withdrawn, and the claims on its accounts are moot: free again, or taken down.
        db.PersonaAccountConflicts.RemoveRange(await db.PersonaAccountConflicts
            .Where(c => c.Status == PersonaConflictStatus.Open && (c.ClaimantPersonaId == id || c.OwnerPersonaId == id))
            .ToListAsync(ct));

        var now = DateTime.UtcNow;
        persona.RemovedAtUtc = now;
        persona.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(
            "remove_persona",
            id,
            persona.Slug,
            new()
            {
                ["takedown"] = takedown ? "true" : "false",
                ["accounts"] = accounts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            ct);
        return new PersonaEditOutcome(PersonaEditResult.Ok, Edited: new PersonaEditedResponse { Id = id, Slug = persona.Slug });
    }
}

/// <summary>Conflict lookups the admin's edits share.</summary>
internal static class PersonaConflictQueries
{
    /// <summary>Open claims other personas make on one of <paramref name="ownerId"/>'s accounts.</summary>
    public static IQueryable<PersonaAccountConflict> OpenOnAccount(TrueMainDbContext db, Guid ownerId, PersonaAccount row)
    {
        var accountId = row.RiotAccountId;
        var nameLower = row.GameName.ToLowerInvariant();
        var tagLower = row.TagLine.ToLowerInvariant();
        return db.PersonaAccountConflicts.Where(c =>
            c.Status == PersonaConflictStatus.Open
            && c.OwnerPersonaId == ownerId
            && ((accountId != null && c.RiotAccountId == accountId)
                || (c.PlatformId == row.PlatformId && c.GameName.ToLower() == nameLower && c.TagLine.ToLower() == tagLower)));
    }
}
