using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Personas;

public interface IPersonaChannelConflictQueryService
{
    /// <summary>
    /// A page of channel conflicts, the most channels in common first, then by name;
    /// <paramref name="search"/> matches either persona's name or slug.
    /// </summary>
    Task<PersonaChannelConflictsReadModel> GetPageAsync(string? search, int? page, int? pageSize, CancellationToken ct);
}

/// <summary>
/// Read side of the channel conflicts (#2286), from <see cref="PersonaChannelConflictFinder"/>: ranked
/// and paged in memory — the open ones are a few hundred at most — then both personas of the page
/// loaded in full, for the admin to compare them without a second request.
/// </summary>
public sealed class PersonaChannelConflictQueryService(TrueMainDbContext db, PersonaChannelConflictFinder finder)
    : IPersonaChannelConflictQueryService
{
    public const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public async Task<PersonaChannelConflictsReadModel> GetPageAsync(
        string? search, int? page, int? pageSize, CancellationToken ct)
    {
        var effectivePage = Math.Clamp(page ?? 1, 1, int.MaxValue / MaxPageSize);
        var effectivePageSize = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var term = search?.Trim() ?? string.Empty;

        var matching = (await finder.FindAsync(ct))
            .Where(c => term.Length == 0 || Matches(c.Older, term) || Matches(c.Newer, term))
            .OrderByDescending(c => c.Channels.Count)
            .ThenBy(c => c.Older.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Older.Slug, StringComparer.Ordinal)
            .ThenBy(c => c.Newer.Slug, StringComparer.Ordinal)
            .ToList();
        var pageRows = matching.Skip((effectivePage - 1) * effectivePageSize).Take(effectivePageSize).ToList();
        var personas = await LoadAsync(pageRows.SelectMany(c => new[] { c.Older.Id, c.Newer.Id }).Distinct().ToList(), ct);

        return new PersonaChannelConflictsReadModel
        {
            Conflicts = pageRows
                .Select(c => new PersonaChannelConflictReadModel
                {
                    Older = personas[c.Older.Id],
                    Newer = personas[c.Newer.Id],
                    SharedChannels = c.Channels
                        .Select(ch => new PersonaSharedChannelReadModel { Kind = ch.Kind.ToString().ToLowerInvariant(), Url = ch.Url })
                        .ToList(),
                })
                .ToList(),
            Total = matching.Count,
            Page = effectivePage,
            PageSize = effectivePageSize,
        };
    }

    private static bool Matches(PersonaChannelSide side, string term) =>
        side.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
        || side.Slug.Contains(term, StringComparison.OrdinalIgnoreCase);

    private async Task<Dictionary<Guid, PersonaOpsReadModel>> LoadAsync(List<Guid> ids, CancellationToken ct)
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
