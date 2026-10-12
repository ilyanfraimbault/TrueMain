using System.Text.RegularExpressions;
using Core.Lol.Identifiers;
using Data;
using Data.Configurations;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;
using TrueMain.Requests.Ops;
using TrueMain.Services.Ops.Accounts;
using TrueMain.Services.Truemains.Identity;

namespace TrueMain.Services.Personas;

public interface IPersonaUpsertService
{
    Task<PersonaUpsertResult> UpsertAsync(PersonaUpsertEntry entry, CancellationToken ct);
}

/// <summary>
/// Write side of <c>POST /ops/personas/batch</c> (#2016): one known player at a time, keyed on its
/// slug. It only ever adds — links and accounts a source stops listing are kept, because a
/// scraper missing a page must not unlink anyone — and it never moves an account that already
/// belongs to another persona: that is reported as a conflict for an operator to settle.
/// </summary>
/// <remarks>
/// Each listed Riot ID is resolved against the tracked accounts on its platform, with the same
/// case-insensitive, most-recently-active rule as <c>TruemainAccountResolver</c>. An untracked one
/// is stored as published and, when the caller asks, queued through the seed intake; every upsert
/// retries the persona's unresolved accounts, so the scraper's next run links what the seed
/// brought in.
/// </remarks>
public sealed partial class PersonaUpsertService(
    TrueMainDbContext db,
    ISeedRequestService seedRequestService,
    ILogger<PersonaUpsertService> logger) : IPersonaUpsertService
{
    public async Task<PersonaUpsertResult> UpsertAsync(PersonaUpsertEntry entry, CancellationToken ct)
    {
        var slug = entry.Slug?.Trim() ?? string.Empty;
        var displayName = entry.DisplayName?.Trim() ?? string.Empty;
        var error = Validate(slug, displayName, entry.Source, out var source);
        if (error is not null)
        {
            return new PersonaUpsertResult { Slug = slug, Error = error };
        }

        var persona = await db.Personas
            .Include(p => p.Links)
            .Include(p => p.Accounts)
            .FirstOrDefaultAsync(p => p.Slug == slug, ct);
        var created = persona is null;
        if (persona is null)
        {
            persona = new Persona { Slug = slug, DisplayName = displayName, Source = source };
            db.Personas.Add(persona);
        }
        else
        {
            persona.DisplayName = displayName;
            persona.UpdatedAtUtc = DateTime.UtcNow;
        }

        MergeLinks(persona, entry.Links ?? [], source);

        var pendingBefore = persona.Accounts.Where(a => a.RiotAccountId is null).ToList();
        var results = new List<PersonaAccountUpsertResult>();
        foreach (var account in entry.Accounts ?? [])
        {
            results.Add(await MergeAccountAsync(persona, account, source, entry.SeedMissing, ct));
        }

        await ResolvePendingAsync(persona, pendingBefore, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // A unique index caught what the checks above could not see: a concurrent writer
            // claimed the same slug, Riot ID or account. Drop this persona's pending changes so
            // the rest of the batch writes cleanly; the caller's next run retries it.
            db.ChangeTracker.Clear();
            logger.LogWarning(ex, "Persona {Slug} was not written: a concurrent write conflicted.", slug);
            return new PersonaUpsertResult { Slug = slug, Error = "conflicting concurrent write; retry." };
        }

        logger.LogInformation(
            "Persona {Slug} {Outcome} from {Source}: {Accounts} accounts, {Linked} linked.",
            slug,
            created ? "created" : "updated",
            source,
            results.Count,
            results.Count(r => r.Status == AccountStatus.Linked));

        return new PersonaUpsertResult { Slug = slug, Created = created, Accounts = results };
    }

    private static string? Validate(string slug, string displayName, string? sourceRaw, out PersonaSource source)
    {
        source = default;
        if (slug.Length is 0 or > PersonaConfiguration.SlugMaxLength || !SlugPattern().IsMatch(slug))
        {
            return "slug must be 1-64 characters of lowercase kebab case ([a-z0-9-]).";
        }

        if (displayName.Length is 0 or > PersonaConfiguration.DisplayNameMaxLength)
        {
            return "displayName is required and at most 64 characters.";
        }

        return Enum.TryParse(sourceRaw?.Trim(), ignoreCase: true, out source) && Enum.IsDefined(source)
            ? null
            : $"source '{sourceRaw}' is not one of {string.Join(", ", Enum.GetNames<PersonaSource>())}.";
    }

    private void MergeLinks(Persona persona, IReadOnlyList<PersonaLinkEntry> links, PersonaSource source)
    {
        foreach (var link in links)
        {
            if (!Enum.TryParse<PersonaLinkKind>(link.Kind?.Trim(), ignoreCase: true, out var kind)
                || !Enum.IsDefined(kind)
                || NormaliseUrl(link.Url) is not { } url)
            {
                logger.LogWarning("Persona {Slug}: skipped malformed link {Kind} {Url}.", persona.Slug, link.Kind, link.Url);
                continue;
            }

            if (persona.Links.Any(existing => string.Equals(existing.Url, url, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            persona.Links.Add(new PersonaLink { Kind = kind, Url = url, Source = source });
        }
    }

    /// <summary>An absolute http(s) URL without a trailing slash, or null when it is not one.</summary>
    internal static string? NormaliseUrl(string? raw)
    {
        if (!Uri.TryCreate(raw?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        var url = uri.GetLeftPart(UriPartial.Query).TrimEnd('/');
        return url.Length <= PersonaLinkConfiguration.UrlMaxLength ? url : null;
    }

    private async Task<PersonaAccountUpsertResult> MergeAccountAsync(
        Persona persona,
        PersonaAccountEntry entry,
        PersonaSource source,
        bool seedMissing,
        CancellationToken ct)
    {
        var gameName = entry.GameName?.Trim() ?? string.Empty;
        var tagLine = entry.TagLine?.Trim().TrimStart('#') ?? string.Empty;
        var result = new PersonaAccountUpsertResult
        {
            GameName = gameName,
            TagLine = tagLine,
            PlatformId = entry.PlatformId?.Trim().ToUpperInvariant() ?? string.Empty,
        };

        if (gameName.Length is 0 or > 32 || tagLine.Length is 0 or > 8
            || !PlatformId.TryParse(result.PlatformId, out var platformId))
        {
            return result with { Status = AccountStatus.Invalid };
        }

        var platform = platformId.Value;
        var nameLower = gameName.ToLowerInvariant();
        var tagLower = tagLine.ToLowerInvariant();

        var listed = persona.Accounts.FirstOrDefault(a =>
            a.PlatformId == platform
            && string.Equals(a.GameName, gameName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.TagLine, tagLine, StringComparison.OrdinalIgnoreCase));
        if (listed is not null)
        {
            if (listed.RiotAccountId is null
                && await ResolveAsync(persona, platform, nameLower, tagLower, ct) is { } resolved)
            {
                if (await IsLinkedElsewhereAsync(persona, resolved, ct))
                {
                    return result with { Status = AccountStatus.Conflict };
                }

                listed.RiotAccountId = resolved;
                listed.ResolvedAtUtc = DateTime.UtcNow;
            }

            return result with { Status = listed.RiotAccountId is null ? AccountStatus.Pending : AccountStatus.Linked };
        }

        var ownedElsewhere = await db.PersonaAccounts.AnyAsync(
            a => a.PlatformId == platform
                 && a.GameName.ToLower() == nameLower
                 && a.TagLine.ToLower() == tagLower,
            ct);
        if (ownedElsewhere)
        {
            return result with { Status = AccountStatus.Conflict };
        }

        var accountId = await ResolveAsync(persona, platform, nameLower, tagLower, ct);
        if (accountId is { } id && await IsLinkedElsewhereAsync(persona, id, ct))
        {
            return result with { Status = AccountStatus.Conflict };
        }

        persona.Accounts.Add(new PersonaAccount
        {
            GameName = gameName,
            TagLine = tagLine,
            PlatformId = platform,
            RiotAccountId = accountId,
            ResolvedAtUtc = accountId is null ? null : DateTime.UtcNow,
            Source = source,
        });

        if (accountId is not null)
        {
            return result with { Status = AccountStatus.Linked };
        }

        // Only on a platform the site shows: seeding an account it could never display would
        // spend Riot budget for nothing.
        if (!seedMissing || RegionFilterParser.RouteToSlug(platform) is null)
        {
            return result with { Status = AccountStatus.Pending };
        }

        var seed = await seedRequestService.CreateAsync(new SeedRequestInput(gameName, tagLine, platform), ct);
        return result with { Status = seed.IsValid ? AccountStatus.Seeded : AccountStatus.Pending };
    }

    /// <summary>
    /// Retries the accounts of the persona no tracked account answered to before this call — the
    /// ones the payload did not list again, since <see cref="MergeAccountAsync"/> resolved those.
    /// </summary>
    private async Task ResolvePendingAsync(Persona persona, List<PersonaAccount> pendingBefore, CancellationToken ct)
    {
        foreach (var pending in pendingBefore.Where(a => a.RiotAccountId is null))
        {
            var id = await ResolveAsync(
                persona, pending.PlatformId, pending.GameName.ToLowerInvariant(), pending.TagLine.ToLowerInvariant(), ct);
            if (id is not null && !await IsLinkedElsewhereAsync(persona, id.Value, ct))
            {
                pending.RiotAccountId = id;
                pending.ResolvedAtUtc = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// The tracked account answering to a Riot ID on one platform — not yet held by another row of
    /// this persona — with the resolver's tiebreak: most recently active first.
    /// </summary>
    private async Task<Guid?> ResolveAsync(
        Persona persona, string platform, string nameLower, string tagLower, CancellationToken ct)
    {
        var id = await db.RiotAccounts
            .AsNoTracking()
            .Where(a => a.PlatformId == platform
                        && a.GameName.ToLower() == nameLower
                        && a.TagLine != null
                        && a.TagLine.ToLower() == tagLower)
            .OrderByDescending(a => a.LastMatchIngestAtUtc ?? a.UpdatedAtUtc)
            .ThenBy(a => a.Id)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);

        return id is not null && persona.Accounts.Any(a => a.RiotAccountId == id) ? null : id;
    }

    private Task<bool> IsLinkedElsewhereAsync(Persona persona, Guid accountId, CancellationToken ct) =>
        db.PersonaAccounts.AnyAsync(a => a.RiotAccountId == accountId && a.PersonaId != persona.Id, ct);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    private static class AccountStatus
    {
        public const string Linked = "linked";
        public const string Pending = "pending";
        public const string Seeded = "seeded";
        public const string Conflict = "conflict";
        public const string Invalid = "invalid";
    }
}
