using System.Text.RegularExpressions;
using Core.Lol.Identifiers;
using Data;
using Data.Configurations;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;
using TrueMain.Requests.Ops;
using TrueMain.Services.Ops.Accounts;

namespace TrueMain.Services.Personas;

public interface IPersonaUpsertService
{
    Task<PersonaUpsertResult> UpsertAsync(PersonaUpsertEntry entry, CancellationToken ct);

    /// <summary>Upserts every entry in order, one result each, and reports the batch once.</summary>
    Task<IReadOnlyList<PersonaUpsertResult>> UpsertBatchAsync(IReadOnlyList<PersonaUpsertEntry> entries, CancellationToken ct);
}

/// <summary>
/// Write side of <c>POST /ops/personas/batch</c> (#2016): one known player at a time, keyed on its
/// slug. It only ever adds — links and accounts a source stops listing are kept, because a
/// scraper missing a page must not unlink anyone — and it never moves an account that already
/// belongs to another persona: that is reported as a conflict and recorded for an operator to
/// settle from the admin (#2253). What the operator settled holds against the next runs: a claim
/// the persona lost is answered <c>excluded</c>, and a merged persona's slug folds into the kept one.
/// The operator's own edits hold the same way (#2019): an unlinked account is an exclusion, a
/// removed link is skipped, a name the operator set is kept, and a removed persona's slug is
/// answered <c>removed</c> with nothing written.
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
    PersonaConflictRecorder conflicts,
    ILogger<PersonaUpsertService> logger) : IPersonaUpsertService
{
    // What a source serialises when it has no value: a JavaScript `undefined` or `null` printed as
    // text (#2134). Never a Riot ID worth storing, let alone seeding.
    private static readonly HashSet<string> PlaceholderNames =
        new(StringComparer.OrdinalIgnoreCase) { "undefined", "null", "none", "nan" };

    public async Task<IReadOnlyList<PersonaUpsertResult>> UpsertBatchAsync(
        IReadOnlyList<PersonaUpsertEntry> entries, CancellationToken ct)
    {
        var results = new List<PersonaUpsertResult>(entries.Count);
        foreach (var entry in entries)
        {
            results.Add(await UpsertAsync(entry, ct));
        }

        // One line per batch, not one per account (#2134): a scraper batch lists dozens of
        // accounts on platforms the pipeline does not ingest, all stored and none seeded.
        var untracked = results
            .SelectMany(r => r.Accounts)
            .Where(a => a.Status == AccountStatus.Untracked)
            .GroupBy(a => a.PlatformId, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}={g.Count()}")
            .ToList();
        if (untracked.Count > 0)
        {
            logger.LogInformation(
                "Persona batch of {Personas}: {Accounts} account(s) on untracked platforms stored without seeding ({Platforms}).",
                results.Count,
                results.Sum(r => r.Accounts.Count(a => a.Status == AccountStatus.Untracked)),
                string.Join(", ", untracked));
        }

        return results;
    }

    public async Task<PersonaUpsertResult> UpsertAsync(PersonaUpsertEntry entry, CancellationToken ct)
    {
        var slug = entry.Slug?.Trim() ?? string.Empty;
        var displayName = entry.DisplayName?.Trim() ?? string.Empty;
        var error = Validate(slug, displayName, entry.Source, out var source);
        if (error is not null)
        {
            return new PersonaUpsertResult { Slug = slug, Error = error };
        }

        var persona = await LoadAsync(p => p.Slug == slug, ct);
        var mergedInto = default(string);
        if (persona?.MergedIntoPersonaId is { } keptId)
        {
            // An operator merged this persona into another: its source keeps sending it under the
            // old slug, which now feeds the kept persona — under the kept persona's own name.
            persona = await LoadAsync(p => p.Id == keptId, ct);
            mergedInto = persona?.Slug;
        }

        if (persona?.RemovedAtUtc is not null)
        {
            // An operator removed this persona (#2019): its sources keep sending it, and the
            // decision holds — nothing is written, not even under the merged slug's kept persona.
            return new PersonaUpsertResult { Slug = slug, Removed = true };
        }

        var created = persona is null;
        if (persona is null)
        {
            persona = new Persona { Slug = slug, DisplayName = displayName, Source = source };
            db.Personas.Add(persona);
        }
        else if (mergedInto is null && !persona.DisplayNameLocked)
        {
            persona.DisplayName = displayName;
        }

        HashSet<string> removedLinks = created ? [] : await conflicts.LoadLinkExclusionsAsync(persona, ct);
        MergeLinks(persona, entry.Links ?? [], source, removedLinks);

        var exclusions = await conflicts.LoadExclusionsAsync(persona, ct);
        var pendingBefore = persona.Accounts.Where(a => a.RiotAccountId is null).ToList();
        var results = new List<PersonaAccountUpsertResult>();
        foreach (var account in entry.Accounts ?? [])
        {
            results.Add(await MergeAccountAsync(persona, account, source, entry.SeedMissing, exclusions, ct));
        }

        await ResolvePendingAsync(persona, pendingBefore, exclusions, ct);

        // The sitemap advertises this stamp as the page's `lastmod` (#1538): the scrapers resend
        // every persona each week, so it moves only when the page does, or all of them claim a
        // change on every run and a crawler learns to ignore the field.
        if (!created && PageChanged(persona))
        {
            persona.UpdatedAtUtc = DateTime.UtcNow;
        }

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
            "Persona {Slug} {Outcome} from {Source}: {Accounts} accounts, {Linked} linked, {Conflicts} in conflict.",
            slug,
            created ? "created" : mergedInto is null ? "updated" : $"folded into {mergedInto}",
            source,
            results.Count,
            results.Count(r => r.Status == AccountStatus.Linked),
            results.Count(r => r.Status == AccountStatus.Conflict));

        return new PersonaUpsertResult { Slug = slug, Created = created, MergedInto = mergedInto, Accounts = results };
    }

    /// <summary>Whether this upsert changed what <c>/players/{slug}</c> shows: the name, a link or an account.</summary>
    private bool PageChanged(Persona persona)
    {
        db.ChangeTracker.DetectChanges();
        return db.Entry(persona).State != EntityState.Unchanged
            || persona.Links.Any(l => db.Entry(l).State != EntityState.Unchanged)
            || persona.Accounts.Any(a => db.Entry(a).State != EntityState.Unchanged);
    }

    private Task<Persona?> LoadAsync(System.Linq.Expressions.Expression<Func<Persona, bool>> match, CancellationToken ct) =>
        db.Personas
            .Include(p => p.Links)
            .Include(p => p.Accounts)
            .FirstOrDefaultAsync(match, ct);

    /// <summary>Why an entry cannot be written as a persona at all, or null when it can.</summary>
    internal static string? Validate(string slug, string displayName, string? sourceRaw, out PersonaSource source)
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

    private void MergeLinks(
        Persona persona, IReadOnlyList<PersonaLinkEntry> links, PersonaSource source, HashSet<string> removedLinks)
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

            // A link an operator removed stays removed (#2019).
            if (removedLinks.Contains(url.ToLowerInvariant())
                || persona.Links.Any(existing => string.Equals(existing.Url, url, StringComparison.OrdinalIgnoreCase)))
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
        PersonaExclusions exclusions,
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
            || PlaceholderNames.Contains(gameName) || PlaceholderNames.Contains(tagLine)
            || !PlatformId.TryParse(result.PlatformId, out var platformId))
        {
            return result with { Status = AccountStatus.Invalid };
        }

        var platform = platformId.Value;
        var nameLower = gameName.ToLowerInvariant();
        var tagLower = tagLine.ToLowerInvariant();
        if (exclusions.Excludes(platform, gameName, tagLine))
        {
            return result with { Status = AccountStatus.Excluded };
        }

        var listed = persona.Accounts.FirstOrDefault(a =>
            a.PlatformId == platform
            && string.Equals(a.GameName, gameName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.TagLine, tagLine, StringComparison.OrdinalIgnoreCase));
        if (listed is not null)
        {
            if (listed.RiotAccountId is null
                && await ResolveAsync(persona, platform, nameLower, tagLower, ct) is { } resolved)
            {
                if (exclusions.Excludes(resolved))
                {
                    return result with { Status = AccountStatus.Excluded };
                }

                if (await HolderAsync(persona, resolved, ct) is { } holder)
                {
                    await conflicts.RecordAsync(persona, holder, listed.GameName, listed.TagLine, platform, resolved, source, ct);
                    return result with { Status = AccountStatus.Conflict };
                }

                listed.RiotAccountId = resolved;
                listed.ResolvedAtUtc = DateTime.UtcNow;
            }

            return result with { Status = listed.RiotAccountId is not null ? AccountStatus.Linked : UnlinkedStatus(platform) };
        }

        var ownedElsewhere = await db.PersonaAccounts
            .AsNoTracking()
            .Where(a => a.PlatformId == platform
                        && a.GameName.ToLower() == nameLower
                        && a.TagLine.ToLower() == tagLower)
            .Select(a => new { a.PersonaId, a.RiotAccountId })
            .FirstOrDefaultAsync(ct);
        if (ownedElsewhere is not null)
        {
            if (ownedElsewhere.RiotAccountId is { } heldId && exclusions.Excludes(heldId))
            {
                return result with { Status = AccountStatus.Excluded };
            }

            await conflicts.RecordAsync(
                persona, ownedElsewhere.PersonaId, gameName, tagLine, platform, ownedElsewhere.RiotAccountId, source, ct);
            return result with { Status = AccountStatus.Conflict };
        }

        var accountId = await ResolveAsync(persona, platform, nameLower, tagLower, ct);
        if (accountId is { } id && exclusions.Excludes(id))
        {
            return result with { Status = AccountStatus.Excluded };
        }

        if (accountId is { } heldElsewhere && await HolderAsync(persona, heldElsewhere, ct) is { } owner)
        {
            await conflicts.RecordAsync(persona, owner, gameName, tagLine, platform, heldElsewhere, source, ct);
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

        // Only on a platform the pipeline ingests (#2134): match ingestion never claims a candidate
        // elsewhere, so a seed there spends Riot budget on an account that is never ingested — and
        // a guessed platform mostly ends on a summoner-v4 404 (#2133).
        if (!SeedRequestService.IsTrackedPlatform(platform))
        {
            return result with { Status = AccountStatus.Untracked };
        }

        if (!seedMissing)
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
    private async Task ResolvePendingAsync(
        Persona persona, List<PersonaAccount> pendingBefore, PersonaExclusions exclusions, CancellationToken ct)
    {
        foreach (var pending in pendingBefore.Where(a => a.RiotAccountId is null))
        {
            var id = await ResolveAsync(
                persona, pending.PlatformId, pending.GameName.ToLowerInvariant(), pending.TagLine.ToLowerInvariant(), ct);
            if (id is not { } resolved || exclusions.Excludes(resolved))
            {
                continue;
            }

            if (await HolderAsync(persona, resolved, ct) is { } holder)
            {
                // The account this Riot ID now answers to was renamed into it while another persona
                // held it under its former name: the same decision as a fresh claim.
                await conflicts.RecordAsync(
                    persona, holder, pending.GameName, pending.TagLine, pending.PlatformId, resolved, pending.Source, ct);
                continue;
            }

            pending.RiotAccountId = resolved;
            pending.ResolvedAtUtc = DateTime.UtcNow;
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

    private static string UnlinkedStatus(string platform) =>
        SeedRequestService.IsTrackedPlatform(platform) ? AccountStatus.Pending : AccountStatus.Untracked;

    /// <summary>The other persona holding the tracked account, or null when none does.</summary>
    private Task<Guid?> HolderAsync(Persona persona, Guid accountId, CancellationToken ct) =>
        db.PersonaAccounts
            .AsNoTracking()
            .Where(a => a.RiotAccountId == accountId && a.PersonaId != persona.Id)
            .Select(a => (Guid?)a.PersonaId)
            .FirstOrDefaultAsync(ct);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    private static class AccountStatus
    {
        public const string Linked = "linked";
        public const string Pending = "pending";
        public const string Untracked = "untracked";
        public const string Seeded = "seeded";
        public const string Conflict = "conflict";
        public const string Excluded = "excluded";
        public const string Invalid = "invalid";
    }
}
