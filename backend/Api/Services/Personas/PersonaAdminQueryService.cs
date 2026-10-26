using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Personas;

public interface IPersonaAdminQueryService
{
    /// <summary>A page of known players, most recently updated first.</summary>
    Task<PersonasPageReadModel> GetPageAsync(PersonaListFilter filter, int? page, int? pageSize, CancellationToken ct);

    /// <summary>One known player in full; null when unknown.</summary>
    Task<PersonaAdminDetailReadModel?> GetAsync(Guid id, CancellationToken ct);
}

/// <summary>Which known players the admin list shows.</summary>
public enum PersonaListView
{
    /// <summary>Every live persona: neither removed nor merged into another.</summary>
    All,

    /// <summary>Live personas in at least one open conflict, over an account or a channel (#2286).</summary>
    Conflicts,

    /// <summary>Live personas with an account no tracked account answers to yet.</summary>
    Unresolved,

    /// <summary>Personas an operator removed.</summary>
    Removed,
}

/// <summary>Filters of the admin list; every one but the view is optional.</summary>
/// <param name="View">Which personas.</param>
/// <param name="Source">The source that first published the persona.</param>
/// <param name="Search">Substring of the slug, the name or one of the listed Riot IDs.</param>
public sealed record PersonaListFilter(PersonaListView View, PersonaSource? Source, string? Search);

/// <summary>Read side of the admin's known-players list and edit panel (#2019).</summary>
public sealed class PersonaAdminQueryService(TrueMainDbContext db, PersonaChannelConflictFinder channelFinder)
    : IPersonaAdminQueryService
{
    public const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public async Task<PersonasPageReadModel> GetPageAsync(
        PersonaListFilter filter, int? page, int? pageSize, CancellationToken ct)
    {
        var effectivePage = Math.Clamp(page ?? 1, 1, int.MaxValue / MaxPageSize);
        var effectivePageSize = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);

        var channelConflicts = await channelFinder.FindAsync(ct);
        var query = Filter(filter, channelConflicts);
        var total = await query.LongCountAsync(ct);
        var conflicts = db.PersonaAccountConflicts.AsNoTracking().Where(c => c.Status == PersonaConflictStatus.Open);
        var rows = await query
            .OrderByDescending(p => p.UpdatedAtUtc)
            .ThenBy(p => p.Slug)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .Select(p => new PersonaListRowReadModel
            {
                Id = p.Id,
                Slug = p.Slug,
                DisplayName = p.DisplayName,
                Source = p.Source.ToString(),
                DisplayNameLocked = p.DisplayNameLocked,
                CreatedAtUtc = p.CreatedAtUtc,
                UpdatedAtUtc = p.UpdatedAtUtc,
                RemovedAtUtc = p.RemovedAtUtc,
                TrackedAccounts = p.Accounts.Count(a => a.RiotAccountId != null),
                UnresolvedAccounts = p.Accounts.Count(a => a.RiotAccountId == null),
                Links = p.Links.Count,
                OpenConflicts = conflicts.Count(c => c.ClaimantPersonaId == p.Id || c.OwnerPersonaId == p.Id),
            })
            .ToListAsync(ct);

        return new PersonasPageReadModel
        {
            Personas = rows
                .Select(r => r with { OpenConflicts = r.OpenConflicts + channelConflicts.Count(c => c.Involves(r.Id)) })
                .ToList(),
            Total = total,
            Page = effectivePage,
            PageSize = effectivePageSize,
        };
    }

    public async Task<PersonaAdminDetailReadModel?> GetAsync(Guid id, CancellationToken ct)
    {
        var persona = await db.Personas
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id,
                p.Slug,
                p.DisplayName,
                p.Source,
                p.DisplayNameLocked,
                p.CreatedAtUtc,
                p.UpdatedAtUtc,
                p.RemovedAtUtc,
                MergedIntoSlug = p.MergedInto == null ? null : p.MergedInto.Slug,
            })
            .FirstOrDefaultAsync(ct);
        if (persona is null)
        {
            return null;
        }

        var accounts = await LoadAccountsAsync(id, ct);
        var links = await db.PersonaLinks
            .AsNoTracking()
            .Where(l => l.PersonaId == id)
            .OrderBy(l => l.Kind)
            .ThenBy(l => l.Url)
            .Select(l => new { l.Id, l.Kind, l.Url, l.Source, l.CreatedAtUtc })
            .ToListAsync(ct);

        var removedLinks = await db.PersonaLinkExclusions
            .AsNoTracking()
            .Where(e => e.PersonaId == id)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Select(e => new PersonaAdminRemovedLinkReadModel { Url = e.Url, CreatedAtUtc = e.CreatedAtUtc })
            .ToListAsync(ct);

        var exclusions = await LoadExclusionsAsync(id, ct);
        var conflicts = await LoadOpenConflictsAsync(id, ct);

        return new PersonaAdminDetailReadModel
        {
            Id = persona.Id,
            Slug = persona.Slug,
            DisplayName = persona.DisplayName,
            Source = persona.Source.ToString(),
            DisplayNameLocked = persona.DisplayNameLocked,
            CreatedAtUtc = persona.CreatedAtUtc,
            UpdatedAtUtc = persona.UpdatedAtUtc,
            RemovedAtUtc = persona.RemovedAtUtc,
            MergedIntoSlug = persona.MergedIntoSlug,
            Accounts = accounts,
            Links = links
                .Select(l => new PersonaAdminLinkReadModel
                {
                    Id = l.Id,
                    Kind = l.Kind.ToString().ToLowerInvariant(),
                    Url = l.Url,
                    Source = l.Source.ToString(),
                    CreatedAtUtc = l.CreatedAtUtc,
                })
                .ToList(),
            Exclusions = exclusions,
            RemovedLinks = removedLinks,
            Conflicts = conflicts,
        };
    }

    /// <summary>Tracked accounts first, then by platform and Riot ID, each with its current Riot ID.</summary>
    private Task<List<PersonaAdminAccountReadModel>> LoadAccountsAsync(Guid id, CancellationToken ct) =>
        db.PersonaAccounts
            .AsNoTracking()
            .Where(a => a.PersonaId == id)
            .OrderBy(a => a.RiotAccountId == null)
            .ThenBy(a => a.PlatformId)
            .ThenBy(a => a.GameName)
            .Select(a => new PersonaAdminAccountReadModel
            {
                Id = a.Id,
                GameName = a.GameName,
                TagLine = a.TagLine,
                PlatformId = a.PlatformId,
                Source = a.Source.ToString(),
                RiotAccountId = a.RiotAccountId,
                CurrentGameName = a.RiotAccount == null ? null : a.RiotAccount.GameName,
                CurrentTagLine = a.RiotAccount == null ? null : a.RiotAccount.TagLine,
                CreatedAtUtc = a.CreatedAtUtc,
            })
            .ToListAsync(ct);

    private Task<List<PersonaAdminExclusionReadModel>> LoadExclusionsAsync(Guid id, CancellationToken ct) =>
        db.PersonaAccountExclusions
            .AsNoTracking()
            .Where(e => e.PersonaId == id)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Select(e => new PersonaAdminExclusionReadModel
            {
                GameName = e.GameName,
                TagLine = e.TagLine,
                PlatformId = e.PlatformId,
                Reason = e.IsTakedown ? "takedown" : e.ConflictId != null ? "conflict" : "unlinked",
                CreatedAtUtc = e.CreatedAtUtc,
            })
            .ToListAsync(ct);

    private Task<List<PersonaAdminConflictReadModel>> LoadOpenConflictsAsync(Guid id, CancellationToken ct) =>
        db.PersonaAccountConflicts
            .AsNoTracking()
            .Where(c => c.Status == PersonaConflictStatus.Open && (c.ClaimantPersonaId == id || c.OwnerPersonaId == id))
            .OrderByDescending(c => c.LastSeenAtUtc)
            .Select(c => new PersonaAdminConflictReadModel
            {
                Id = c.Id,
                Role = c.ClaimantPersonaId == id ? "claimant" : "owner",
                GameName = c.GameName,
                TagLine = c.TagLine,
                PlatformId = c.PlatformId,
                OtherSlug = c.ClaimantPersonaId == id ? c.Owner!.Slug : c.Claimant!.Slug,
                OtherDisplayName = c.ClaimantPersonaId == id ? c.Owner!.DisplayName : c.Claimant!.DisplayName,
                Source = c.Source.ToString(),
                LastSeenAtUtc = c.LastSeenAtUtc,
            })
            .ToListAsync(ct);

    private IQueryable<Persona> Filter(PersonaListFilter filter, IReadOnlyList<PersonaChannelConflict> channelConflicts)
    {
        var query = db.Personas.AsNoTracking();
        var inChannelConflict = channelConflicts.SelectMany(c => new[] { c.Older.Id, c.Newer.Id }).Distinct().ToList();
        var openConflicts = db.PersonaAccountConflicts.AsNoTracking().Where(c => c.Status == PersonaConflictStatus.Open);
        query = filter.View switch
        {
            PersonaListView.Removed => query.Where(p => p.RemovedAtUtc != null),
            PersonaListView.Conflicts => Live(query).Where(p =>
                openConflicts.Any(c => c.ClaimantPersonaId == p.Id || c.OwnerPersonaId == p.Id)
                || inChannelConflict.Contains(p.Id)),
            PersonaListView.Unresolved => Live(query).Where(p => p.Accounts.Any(a => a.RiotAccountId == null)),
            _ => Live(query),
        };

        if (filter.Source is { } source)
        {
            query = query.Where(p => p.Source == source);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // 3-argument ILike: the 2-argument form emits ESCAPE '' and leaves the escaping inert.
            var pattern = $"%{EscapeLike(filter.Search.Trim())}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.Slug, pattern, "\\")
                || EF.Functions.ILike(p.DisplayName, pattern, "\\")
                || p.Accounts.Any(a => EF.Functions.ILike(a.GameName, pattern, "\\")));
        }

        return query;
    }

    private static IQueryable<Persona> Live(IQueryable<Persona> query) =>
        query.Where(p => p.RemovedAtUtc == null && p.MergedIntoPersonaId == null);

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
