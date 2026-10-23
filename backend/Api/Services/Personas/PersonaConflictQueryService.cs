using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Personas;

public interface IPersonaConflictQueryService
{
    /// <summary>A page of conflicts, most recently seen first, with the table's open/resolved counts.</summary>
    Task<PersonaConflictsReadModel> GetPageAsync(PersonaConflictFilter filter, int? page, int? pageSize, CancellationToken ct);

    /// <summary>One conflict and both personas in full; null when unknown.</summary>
    Task<PersonaConflictDetailReadModel?> GetAsync(Guid id, CancellationToken ct);
}

/// <summary>Filters of the conflict queue; every one is optional.</summary>
/// <param name="Status">Open or Resolved; null for both.</param>
/// <param name="Source">The source of the claim.</param>
/// <param name="PlatformId">A normalised platform id.</param>
/// <param name="Search">Substring of either persona's slug or name, or of the Riot ID.</param>
public sealed record PersonaConflictFilter(
    PersonaConflictStatus? Status, PersonaSource? Source, string? PlatformId, string? Search);

/// <summary>Read side of the admin's persona conflict queue (#2253).</summary>
public sealed class PersonaConflictQueryService(TrueMainDbContext db) : IPersonaConflictQueryService
{
    public const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public async Task<PersonaConflictsReadModel> GetPageAsync(
        PersonaConflictFilter filter, int? page, int? pageSize, CancellationToken ct)
    {
        var effectivePage = Math.Clamp(page ?? 1, 1, int.MaxValue / MaxPageSize);
        var effectivePageSize = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);

        var query = Filter(db.PersonaAccountConflicts.AsNoTracking(), filter);
        var total = await query.LongCountAsync(ct);
        var rows = await Project(query
                .OrderByDescending(c => c.LastSeenAtUtc)
                .ThenBy(c => c.Id)
                .Skip((effectivePage - 1) * effectivePageSize)
                .Take(effectivePageSize))
            .ToListAsync(ct);

        var counts = await db.PersonaAccountConflicts
            .AsNoTracking()
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.LongCount() })
            .ToListAsync(ct);

        return new PersonaConflictsReadModel
        {
            Conflicts = rows,
            Total = total,
            Page = effectivePage,
            PageSize = effectivePageSize,
            Open = counts.FirstOrDefault(c => c.Status == PersonaConflictStatus.Open)?.Count ?? 0,
            Resolved = counts.FirstOrDefault(c => c.Status == PersonaConflictStatus.Resolved)?.Count ?? 0,
        };
    }

    public async Task<PersonaConflictDetailReadModel?> GetAsync(Guid id, CancellationToken ct)
    {
        var conflict = await Project(db.PersonaAccountConflicts.AsNoTracking().Where(c => c.Id == id))
            .FirstOrDefaultAsync(ct);
        if (conflict is null)
        {
            return null;
        }

        return new PersonaConflictDetailReadModel
        {
            Conflict = conflict,
            Claimant = await LoadPersonaAsync(conflict.Claimant.Id, conflict.Account, ct),
            Owner = await LoadPersonaAsync(conflict.Owner.Id, conflict.Account, ct),
        };
    }

    private static IQueryable<PersonaAccountConflict> Filter(IQueryable<PersonaAccountConflict> query, PersonaConflictFilter filter)
    {
        if (filter.Status is { } status)
        {
            query = query.Where(c => c.Status == status);
        }

        if (filter.Source is { } source)
        {
            query = query.Where(c => c.Source == source);
        }

        if (filter.PlatformId is { } platform)
        {
            query = query.Where(c => c.PlatformId == platform);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // 3-argument ILike: the 2-argument form emits ESCAPE '' and leaves the escaping inert.
            var pattern = $"%{EscapeLike(filter.Search.Trim())}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.GameName, pattern, "\\")
                || EF.Functions.ILike(c.TagLine, pattern, "\\")
                || EF.Functions.ILike(c.Claimant!.Slug, pattern, "\\")
                || EF.Functions.ILike(c.Claimant!.DisplayName, pattern, "\\")
                || EF.Functions.ILike(c.Owner!.Slug, pattern, "\\")
                || EF.Functions.ILike(c.Owner!.DisplayName, pattern, "\\"));
        }

        return query;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static IQueryable<PersonaConflictReadModel> Project(IQueryable<PersonaAccountConflict> query) =>
        query.Select(c => new PersonaConflictReadModel
        {
            Id = c.Id,
            Status = c.Status.ToString(),
            Resolution = c.Resolution == null ? null : c.Resolution.ToString(),
            Source = c.Source.ToString(),
            FirstSeenAtUtc = c.FirstSeenAtUtc,
            LastSeenAtUtc = c.LastSeenAtUtc,
            TimesSeen = c.TimesSeen,
            ResolvedAtUtc = c.ResolvedAtUtc,
            Account = new PersonaConflictAccountReadModel
            {
                GameName = c.GameName,
                TagLine = c.TagLine,
                PlatformId = c.PlatformId,
                RiotAccountId = c.RiotAccountId,
                CurrentGameName = c.RiotAccount == null ? null : c.RiotAccount.GameName,
                CurrentTagLine = c.RiotAccount == null ? null : c.RiotAccount.TagLine,
            },
            Claimant = new PersonaConflictPersonaReadModel
            {
                Id = c.ClaimantPersonaId,
                Slug = c.Claimant!.Slug,
                DisplayName = c.Claimant.DisplayName,
                Source = c.Claimant.Source.ToString(),
                AccountCount = c.Claimant.Accounts.Count,
                LinkCount = c.Claimant.Links.Count,
            },
            Owner = new PersonaConflictPersonaReadModel
            {
                Id = c.OwnerPersonaId,
                Slug = c.Owner!.Slug,
                DisplayName = c.Owner.DisplayName,
                Source = c.Owner.Source.ToString(),
                AccountCount = c.Owner.Accounts.Count,
                LinkCount = c.Owner.Links.Count,
            },
        });

    private async Task<PersonaOpsReadModel> LoadPersonaAsync(
        Guid personaId, PersonaConflictAccountReadModel disputed, CancellationToken ct)
    {
        var persona = await db.Personas
            .AsNoTracking()
            .Where(p => p.Id == personaId)
            .Select(p => new { p.Id, p.Slug, p.DisplayName, p.Source, p.CreatedAtUtc })
            .FirstAsync(ct);

        var accounts = await db.PersonaAccounts
            .AsNoTracking()
            .Where(a => a.PersonaId == personaId)
            .OrderBy(a => a.PlatformId)
            .ThenBy(a => a.GameName)
            .Select(a => new { a.GameName, a.TagLine, a.PlatformId, a.Source, a.RiotAccountId })
            .ToListAsync(ct);

        var links = await db.PersonaLinks
            .AsNoTracking()
            .Where(l => l.PersonaId == personaId)
            .OrderBy(l => l.Kind)
            .ThenBy(l => l.Url)
            .Select(l => new { l.Kind, l.Url, l.Source })
            .ToListAsync(ct);

        return new PersonaOpsReadModel
        {
            Id = persona.Id,
            Slug = persona.Slug,
            DisplayName = persona.DisplayName,
            Source = persona.Source.ToString(),
            CreatedAtUtc = persona.CreatedAtUtc,
            Accounts = accounts
                .Select(a => new PersonaOpsAccountReadModel
                {
                    GameName = a.GameName,
                    TagLine = a.TagLine,
                    PlatformId = a.PlatformId,
                    Source = a.Source.ToString(),
                    RiotAccountId = a.RiotAccountId,
                    Disputed = IsDisputed(a.PlatformId, a.GameName, a.TagLine, a.RiotAccountId, disputed),
                })
                .ToList(),
            Links = links
                .Select(l => new PersonaOpsLinkReadModel
                {
                    Kind = l.Kind.ToString().ToLowerInvariant(),
                    Url = l.Url,
                    Source = l.Source.ToString(),
                })
                .ToList(),
        };
    }

    private static bool IsDisputed(
        string platformId, string gameName, string tagLine, Guid? riotAccountId, PersonaConflictAccountReadModel disputed) =>
        (riotAccountId is not null && riotAccountId == disputed.RiotAccountId)
        || (platformId == disputed.PlatformId
            && string.Equals(gameName, disputed.GameName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(tagLine, disputed.TagLine, StringComparison.OrdinalIgnoreCase));
}
