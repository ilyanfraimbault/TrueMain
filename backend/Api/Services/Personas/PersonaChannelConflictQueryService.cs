using Data;
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
        var personas = await PersonaOpsReads.LoadAsync(db, pageRows.SelectMany(c => new[] { c.Older.Id, c.Newer.Id }).Distinct().ToList(), ct);

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
}
