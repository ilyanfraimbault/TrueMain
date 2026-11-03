using Data;
using Data.Configurations;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Personas;

public interface IPersonaHomonymQueryService
{
    /// <summary>
    /// A page of homonym groups, the ones sharing a link first (the likeliest duplicates), then in name
    /// order; <paramref name="search"/> matches a name or slug.
    /// </summary>
    Task<PersonaHomonymsReadModel> GetPageAsync(string? search, int? page, int? pageSize, CancellationToken ct);

    /// <summary>How many homonym groups wait for the operator: the queue's total, unfiltered (#2294).</summary>
    Task<long> CountPendingAsync(CancellationToken ct);
}

/// <summary>
/// Read side of the admin's homonyms queue (#2271): the duplicates a conflict cannot reveal, two
/// sources publishing one person under two slugs with no account in common — and the true
/// homonyms Leaguepedia lists, which only the operator can tell apart. Grouped by
/// <see cref="PersonaHomonyms"/>'s name key over the live personas.
/// </summary>
public sealed class PersonaHomonymQueryService(TrueMainDbContext db) : IPersonaHomonymQueryService
{
    public const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    private static readonly string LiveSql = $$"""
        SELECT p."Id", p."DisplayName", p."Slug", {{PersonaHomonyms.NameKeySql("p")}} AS key
        FROM personas p
        WHERE {{PersonaConfiguration.LiveFilterSql}}
        """;

    // A group stays while one pair in it is not marked: n personas make n(n-1)/2 pairs. `live` is
    // the caller's, so the count reads only the ids and keys the name-key index holds (#2294).
    private const string PendingGroupsSql = """
        groups AS (
            SELECT key, count(*) AS n FROM live WHERE key <> '' GROUP BY key HAVING count(*) > 1
        ),
        marked AS (
            SELECT a.key, count(*) AS m
            FROM persona_distinct_pairs d
            JOIN live a ON a."Id" = d."PersonaId"
            JOIN live b ON b."Id" = d."OtherPersonaId" AND b.key = a.key
            GROUP BY a.key
        )
        """;

    private const string PendingFilterSql = "coalesce(m.m, 0) < g.n * (g.n - 1) / 2";

    private static readonly string PendingCountSql = $$"""
        WITH live AS (
            SELECT p."Id", {{PersonaHomonyms.NameKeySql("p")}} AS key FROM personas p WHERE {{PersonaConfiguration.LiveFilterSql}}
        ),
        {{PendingGroupsSql}}
        SELECT count(*) AS "Value"
        FROM groups g
        LEFT JOIN marked m ON m.key = g.key
        WHERE {{PendingFilterSql}}
        """;

    // Two of a group's personas listing the same channel is the strongest hint they are one person:
    // those come first. Only the grouped personas' links are keyed: keying all of them made the
    // query an order of magnitude slower.
    private static readonly string GroupKeysSql = $$"""
        WITH live AS ({{LiveSql}}),
        {{PendingGroupsSql}},
        links AS (
            SELECT live.key, l."PersonaId", {{PersonaHomonyms.LinkKeySql("l")}} AS url
            FROM persona_links l
            JOIN live ON live."Id" = l."PersonaId"
            JOIN groups ON groups.key = live.key
        ),
        shared AS (
            SELECT DISTINCT a.key
            FROM links a
            JOIN links b ON b.key = a.key AND b.url = a.url AND b."PersonaId" <> a."PersonaId"
        )
        SELECT g.key AS "Key", (s.key IS NOT NULL) AS "SharesALink"
        FROM groups g
        LEFT JOIN marked m ON m.key = g.key
        LEFT JOIN shared s ON s.key = g.key
        WHERE {{PendingFilterSql}}
          AND ({0} = '' OR EXISTS (
              SELECT 1 FROM live l
              WHERE l.key = g.key AND (l."DisplayName" ILIKE {0} ESCAPE '\' OR l."Slug" ILIKE {0} ESCAPE '\')))
        """;

    private static readonly string MembersSql = $$"""SELECT "Id", key AS "Key" FROM ({{LiveSql}}) live WHERE key = ANY({0})""";

    public async Task<PersonaHomonymsReadModel> GetPageAsync(string? search, int? page, int? pageSize, CancellationToken ct)
    {
        var effectivePage = Math.Clamp(page ?? 1, 1, int.MaxValue / MaxPageSize);
        var effectivePageSize = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var pattern = string.IsNullOrWhiteSpace(search) ? string.Empty : $"%{PersonaConflictQueryService.EscapeLike(search.Trim())}%";

        var keys = db.Database.SqlQueryRaw<GroupKey>(GroupKeysSql, pattern);
        var total = await keys.LongCountAsync(ct);
        var pageKeys = await keys
            .OrderByDescending(k => k.SharesALink)
            .ThenBy(k => k.Key)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .Select(k => k.Key)
            .ToListAsync(ct);

        return new PersonaHomonymsReadModel
        {
            Groups = pageKeys.Count == 0 ? [] : await LoadGroupsAsync(pageKeys, ct),
            Total = total,
            Page = effectivePage,
            PageSize = effectivePageSize,
        };
    }

    public Task<long> CountPendingAsync(CancellationToken ct) =>
        db.Database.SqlQueryRaw<long>(PendingCountSql).SingleAsync(ct);

    private async Task<IReadOnlyList<PersonaHomonymGroupReadModel>> LoadGroupsAsync(List<string> keys, CancellationToken ct)
    {
        var members = await db.Database
            .SqlQueryRaw<Member>(MembersSql, new object[] { keys.ToArray() })
            .ToListAsync(ct);
        var ids = members.Select(m => m.Id).ToList();

        var personas = (await PersonaOpsReads.LoadAsync(db, ids, ct)).Values.ToList();
        var pairs = await db.PersonaDistinctPairs
            .AsNoTracking()
            .Where(p => ids.Contains(p.PersonaId) && ids.Contains(p.OtherPersonaId))
            .Select(p => new { p.PersonaId, p.OtherPersonaId })
            .ToListAsync(ct);

        var keyOf = members.ToDictionary(m => m.Id, m => m.Key);
        return keys
            .Select(key =>
            {
                var group = personas
                    .Where(p => keyOf[p.Id] == key)
                    .OrderBy(p => p.CreatedAtUtc)
                    .ThenBy(p => p.Slug, StringComparer.Ordinal)
                    .Select(p => new PersonaHomonymReadModel
                    {
                        Id = p.Id,
                        Slug = p.Slug,
                        DisplayName = p.DisplayName,
                        Source = p.Source,
                        CreatedAtUtc = p.CreatedAtUtc,
                        Accounts = p.Accounts,
                        Links = p.Links,
                    })
                    .ToList();
                return new PersonaHomonymGroupReadModel
                {
                    Key = key,
                    Personas = group,
                    SharedLinks = group
                        .SelectMany(p => p.Links.Select(l => (p.Id, Key: PersonaHomonyms.LinkKey(l.Url))).Distinct())
                        .GroupBy(l => l.Key, StringComparer.Ordinal)
                        .Where(g => g.Count() > 1)
                        .Select(g => g.Key)
                        .Order(StringComparer.Ordinal)
                        .ToList(),
                    DistinctPairs = pairs
                        .Where(p => keyOf[p.PersonaId] == key && keyOf[p.OtherPersonaId] == key)
                        .Select(p => (IReadOnlyList<Guid>)[p.PersonaId, p.OtherPersonaId])
                        .ToList(),
                };
            })
            .ToList();
    }

    private sealed record Member(Guid Id, string Key);

    private sealed record GroupKey(string Key, bool SharesALink);
}
