using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Personas;

public interface IPersonaConflictQueryService
{
    /// <summary>
    /// A page of conflicts with the table's open/resolved counts: the open queue most certain first
    /// (#2277), anything else most recently seen first.
    /// </summary>
    Task<PersonaConflictsReadModel> GetPageAsync(PersonaConflictFilter filter, int? page, int? pageSize, CancellationToken ct);

    /// <summary>
    /// The open conflicts a bulk merge at <paramref name="minScore"/> would settle (#2295), most
    /// certain first, at most <see cref="PersonaConflictBulk.BatchLimit"/>, each with the side it keeps.
    /// </summary>
    Task<PersonaConflictBulkPreviewReadModel> GetBulkPreviewAsync(int minScore, CancellationToken ct);

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
        var skip = (effectivePage - 1) * effectivePageSize;
        List<PersonaConflictReadModel> rows;
        if (filter.Status == PersonaConflictStatus.Open)
        {
            // The queue to settle reads most certain first (#2277).
            var ranked = await RankOpenAsync(query, ct);
            rows = await ProjectRankedAsync(query, ranked.Skip(skip).Take(effectivePageSize).ToList(), ct);
        }
        else
        {
            rows = await Project(query
                    .OrderByDescending(c => c.LastSeenAtUtc)
                    .ThenBy(c => c.Id)
                    .Skip(skip)
                    .Take(effectivePageSize))
                .ToListAsync(ct);
            var evidence = await EvidenceAsync(rows.Select(c => (c.Id, c.Claimant.Id, c.Owner.Id)), ct);
            rows = rows.Select(c => WithEvidence(c, evidence[c.Id])).ToList();
        }

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

    public async Task<PersonaConflictBulkPreviewReadModel> GetBulkPreviewAsync(int minScore, CancellationToken ct)
    {
        var query = db.PersonaAccountConflicts.AsNoTracking().Where(c => c.Status == PersonaConflictStatus.Open);
        var eligible = (await RankOpenAsync(query, ct)).Where(c => PersonaConflictBulk.IsEligible(c.Evidence, minScore)).ToList();
        var rows = await ProjectRankedAsync(query, eligible.Take(PersonaConflictBulk.BatchLimit).ToList(), ct);
        return new PersonaConflictBulkPreviewReadModel
        {
            MinScore = minScore,
            Limit = PersonaConflictBulk.BatchLimit,
            Eligible = eligible.Count,
            Conflicts = rows.Select(c =>
            {
                var keepClaimant = PersonaConflictBulk.KeepClaimant(c.Claimant.CreatedAtUtc, c.Claimant.Id, c.Owner.CreatedAtUtc, c.Owner.Id);
                return new PersonaConflictBulkCandidateReadModel
                {
                    Conflict = c,
                    Keep = keepClaimant ? "claimant" : "owner",
                    KeptSlug = keepClaimant ? c.Claimant.Slug : c.Owner.Slug,
                    MergedSlug = keepClaimant ? c.Owner.Slug : c.Claimant.Slug,
                };
            }).ToList(),
        };
    }

    /// <summary>
    /// The open conflicts most certain first (#2277). The score comes from both personas' links, so
    /// it is ranked here: an open queue holds dozens of rows, not millions.
    /// </summary>
    private async Task<List<(Guid Id, PersonaConflictEvidence Evidence)>> RankOpenAsync(
        IQueryable<PersonaAccountConflict> query, CancellationToken ct)
    {
        var open = await query
            .Select(c => new { c.Id, c.ClaimantPersonaId, c.OwnerPersonaId, c.LastSeenAtUtc })
            .ToListAsync(ct);
        var evidence = await EvidenceAsync(open.Select(c => (c.Id, c.ClaimantPersonaId, c.OwnerPersonaId)), ct);
        return open
            .OrderByDescending(c => evidence[c.Id].Score)
            .ThenByDescending(c => c.LastSeenAtUtc)
            .ThenBy(c => c.Id)
            .Select(c => (c.Id, evidence[c.Id]))
            .ToList();
    }

    private static async Task<List<PersonaConflictReadModel>> ProjectRankedAsync(
        IQueryable<PersonaAccountConflict> query, List<(Guid Id, PersonaConflictEvidence Evidence)> ranked, CancellationToken ct)
    {
        var ids = ranked.Select(c => c.Id).ToList();
        var byId = (await Project(query.Where(c => ids.Contains(c.Id))).ToListAsync(ct)).ToDictionary(c => c.Id);
        return ranked.Where(c => byId.ContainsKey(c.Id)).Select(c => WithEvidence(byId[c.Id], c.Evidence)).ToList();
    }

    public async Task<PersonaConflictDetailReadModel?> GetAsync(Guid id, CancellationToken ct)
    {
        var conflict = await Project(db.PersonaAccountConflicts.AsNoTracking().Where(c => c.Id == id))
            .FirstOrDefaultAsync(ct);
        if (conflict is null)
        {
            return null;
        }

        var evidence = await EvidenceAsync([(conflict.Id, conflict.Claimant.Id, conflict.Owner.Id)], ct);
        return new PersonaConflictDetailReadModel
        {
            Conflict = WithEvidence(conflict, evidence[conflict.Id]),
            Claimant = await LoadPersonaAsync(conflict.Claimant.Id, conflict.Account, ct),
            Owner = await LoadPersonaAsync(conflict.Owner.Id, conflict.Account, ct),
        };
    }

    private Task<Dictionary<Guid, PersonaConflictEvidence>> EvidenceAsync(
        IEnumerable<(Guid Id, Guid ClaimantId, Guid OwnerId)> conflicts, CancellationToken ct) =>
        PersonaConflictBulk.EvidenceAsync(db, conflicts, ct);

    private static PersonaConflictReadModel WithEvidence(PersonaConflictReadModel conflict, PersonaConflictEvidence evidence) =>
        conflict with
        {
            Evidence = new PersonaConflictEvidenceReadModel
            {
                Score = evidence.Score,
                SharedSocial = evidence.SharedSocial.Select(k => k.ToString().ToLowerInvariant()).ToList(),
                SharedReferences = evidence.SharedReferences,
                DifferingSocial = evidence.DifferingSocial.Select(k => k.ToString().ToLowerInvariant()).ToList(),
            },
        };

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

    internal static string EscapeLike(string value) =>
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
                CreatedAtUtc = c.Claimant.CreatedAtUtc,
                AccountCount = c.Claimant.Accounts.Count,
                LinkCount = c.Claimant.Links.Count,
            },
            Owner = new PersonaConflictPersonaReadModel
            {
                Id = c.OwnerPersonaId,
                Slug = c.Owner!.Slug,
                DisplayName = c.Owner.DisplayName,
                Source = c.Owner.Source.ToString(),
                CreatedAtUtc = c.Owner.CreatedAtUtc,
                AccountCount = c.Owner.Accounts.Count,
                LinkCount = c.Owner.Links.Count,
            },
        });

    private async Task<PersonaOpsReadModel> LoadPersonaAsync(
        Guid personaId, PersonaConflictAccountReadModel disputed, CancellationToken ct)
    {
        var persona = (await PersonaOpsReads.LoadAsync(db, [personaId], ct))[personaId];
        return persona with
        {
            Accounts = persona.Accounts
                .Select(a => a with { Disputed = IsDisputed(a.PlatformId, a.GameName, a.TagLine, a.RiotAccountId, disputed) })
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
