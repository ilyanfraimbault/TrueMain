using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Personas;

/// <summary>
/// Known players in full for the admin's queues — the account and channel conflicts and the homonyms
/// (#2253, #2271, #2286): every account and link with its source, accounts by platform then name,
/// links by kind then URL. Three queries for any number of personas.
/// </summary>
public static class PersonaOpsReads
{
    public static async Task<Dictionary<Guid, PersonaOpsReadModel>> LoadAsync(
        TrueMainDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var personas = await db.Personas
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Slug, p.DisplayName, p.Source, p.CreatedAtUtc })
            .ToListAsync(ct);
        var accounts = (await db.PersonaAccounts
                .AsNoTracking()
                .Where(a => ids.Contains(a.PersonaId))
                .OrderBy(a => a.PlatformId)
                .ThenBy(a => a.GameName)
                .Select(a => new { a.PersonaId, a.GameName, a.TagLine, a.PlatformId, a.Source, a.RiotAccountId })
                .ToListAsync(ct))
            .ToLookup(a => a.PersonaId);
        var links = (await db.PersonaLinks
                .AsNoTracking()
                .Where(l => ids.Contains(l.PersonaId))
                .OrderBy(l => l.Kind)
                .ThenBy(l => l.Url)
                .Select(l => new { l.PersonaId, l.Kind, l.Url, l.Source })
                .ToListAsync(ct))
            .ToLookup(l => l.PersonaId);

        return personas.ToDictionary(
            p => p.Id,
            p => new PersonaOpsReadModel
            {
                Id = p.Id,
                Slug = p.Slug,
                DisplayName = p.DisplayName,
                Source = p.Source.ToString(),
                CreatedAtUtc = p.CreatedAtUtc,
                Accounts = accounts[p.Id]
                    .Select(a => new PersonaOpsAccountReadModel
                    {
                        GameName = a.GameName,
                        TagLine = a.TagLine,
                        PlatformId = a.PlatformId,
                        Source = a.Source.ToString(),
                        RiotAccountId = a.RiotAccountId,
                    })
                    .ToList(),
                Links = links[p.Id]
                    .Select(l => new PersonaOpsLinkReadModel
                    {
                        Kind = l.Kind.ToString().ToLowerInvariant(),
                        Url = l.Url,
                        Source = l.Source.ToString(),
                    })
                    .ToList(),
            });
    }
}
