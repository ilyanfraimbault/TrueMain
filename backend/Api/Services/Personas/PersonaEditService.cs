using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;
using TrueMain.Requests.Ops;

namespace TrueMain.Services.Personas;

public interface IPersonaEditService
{
    /// <summary>Adds a known player by hand, every row with the source <c>manual</c>.</summary>
    Task<PersonaEditOutcome> CreateAsync(PersonaCreateRequest request, CancellationToken ct);

    /// <summary>Sets the operator's name for the person, which the sources then no longer overwrite.</summary>
    Task<PersonaEditOutcome> RenameAsync(Guid id, PersonaRenameRequest request, CancellationToken ct);

    /// <summary>Adds a link by hand, lifting a removal of the same URL.</summary>
    Task<PersonaEditOutcome> AddLinkAsync(Guid id, PersonaLinkEntry request, CancellationToken ct);

    /// <summary>Removes a link; the sources can no longer add it back.</summary>
    Task<PersonaEditOutcome> RemoveLinkAsync(Guid id, Guid linkId, CancellationToken ct);

    /// <summary>Adds an account by hand through the intake's own rules, lifting the persona's exclusion of it.</summary>
    Task<PersonaEditOutcome> AddAccountAsync(Guid id, PersonaAccountEntry request, CancellationToken ct);

    /// <summary>Unlinks an account; the persona's sources can no longer link it back.</summary>
    Task<PersonaEditOutcome> UnlinkAccountAsync(Guid id, Guid accountId, CancellationToken ct);
}

public enum PersonaEditResult
{
    Ok,
    NotFound,
    Invalid,

    /// <summary>The slug or link already exists, or the persona was merged or removed meanwhile.</summary>
    Conflict,
}

public sealed record PersonaEditOutcome(PersonaEditResult Result, string? Error = null, PersonaEditedResponse? Edited = null);

/// <summary>
/// The operator's edits of a known player from the admin (#2019). Each one is written where the
/// weekly scrapers cannot undo it — an unlinked account becomes an exclusion, a removed link a
/// link exclusion, a name the operator set is locked — and each is audited. Accounts go through
/// <see cref="IPersonaUpsertService"/>, so a manual account resolves, seeds and raises conflicts
/// exactly like a scraped one.
/// </summary>
public sealed class PersonaEditService(
    TrueMainDbContext db,
    IPersonaUpsertService upsertService,
    PersonaAudit audit) : IPersonaEditService
{
    private const string ManualSource = nameof(PersonaSource.Manual);

    public async Task<PersonaEditOutcome> CreateAsync(PersonaCreateRequest request, CancellationToken ct)
    {
        var slug = request.Slug?.Trim() ?? string.Empty;
        if (PersonaUpsertService.Validate(slug, request.DisplayName?.Trim() ?? string.Empty, ManualSource, out _) is { } invalid)
        {
            return new PersonaEditOutcome(PersonaEditResult.Invalid, invalid);
        }

        if (await db.Personas.AsNoTracking().AnyAsync(p => p.Slug == slug, ct))
        {
            return new PersonaEditOutcome(
                PersonaEditResult.Conflict, $"The slug '{slug}' is already used by a known player, live, merged or removed.");
        }

        if ((request.Links ?? []).FirstOrDefault(l => ParseLink(l) is null) is { } badLink)
        {
            return Invalid(badLink);
        }

        var result = await upsertService.UpsertAsync(
            new PersonaUpsertEntry
            {
                Slug = slug,
                DisplayName = request.DisplayName,
                Source = ManualSource,
                Links = request.Links,
                Accounts = request.Accounts,
                SeedMissing = true,
            },
            ct);
        if (result.Error is { } error)
        {
            // Validated above: what is left is a concurrent writer that took the slug or an account.
            return new PersonaEditOutcome(PersonaEditResult.Conflict, error);
        }

        // The operator's name from now on: the sources no longer overwrite it.
        var id = await db.Personas.AsNoTracking().Where(p => p.Slug == slug).Select(p => p.Id).FirstAsync(ct);
        await db.Personas.Where(p => p.Id == id).ExecuteUpdateAsync(set => set.SetProperty(p => p.DisplayNameLocked, true), ct);
        await audit.RecordAsync("create_persona", id, slug, new() { ["accounts"] = result.Accounts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) }, ct);
        return Ok(id, slug, result.Accounts);
    }

    public async Task<PersonaEditOutcome> RenameAsync(Guid id, PersonaRenameRequest request, CancellationToken ct)
    {
        var name = request.DisplayName?.Trim() ?? string.Empty;
        if (name.Length is 0 or > Data.Configurations.PersonaConfiguration.DisplayNameMaxLength)
        {
            return new PersonaEditOutcome(PersonaEditResult.Invalid, "displayName is required and at most 64 characters.");
        }

        var persona = await db.Personas.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (NotLive(persona) is { } refused)
        {
            return refused;
        }

        var previous = persona!.DisplayName;
        persona.DisplayName = name;
        persona.DisplayNameLocked = true;
        persona.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("rename_persona", id, persona.Slug, new() { ["from"] = previous, ["to"] = name }, ct);
        return Ok(id, persona.Slug);
    }

    public async Task<PersonaEditOutcome> AddLinkAsync(Guid id, PersonaLinkEntry request, CancellationToken ct)
    {
        if (ParseLink(request) is not { } link)
        {
            return Invalid(request);
        }

        var persona = await db.Personas.Include(p => p.Links).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (NotLive(persona) is { } refused)
        {
            return refused;
        }

        if (persona!.Links.Any(l => string.Equals(l.Url, link.Url, StringComparison.OrdinalIgnoreCase)))
        {
            return new PersonaEditOutcome(PersonaEditResult.Conflict, "This link is already listed.");
        }

        var urlLower = link.Url.ToLowerInvariant();
        db.PersonaLinkExclusions.RemoveRange(
            await db.PersonaLinkExclusions.Where(e => e.PersonaId == id && e.Url.ToLower() == urlLower).ToListAsync(ct));
        persona.Links.Add(new PersonaLink { Kind = link.Kind, Url = link.Url, Source = PersonaSource.Manual });
        persona.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("add_persona_link", id, persona.Slug, new() { ["url"] = link.Url }, ct);
        return Ok(id, persona.Slug);
    }

    public async Task<PersonaEditOutcome> RemoveLinkAsync(Guid id, Guid linkId, CancellationToken ct)
    {
        var persona = await db.Personas.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (NotLive(persona) is { } refused)
        {
            return refused;
        }

        var link = await db.PersonaLinks.FirstOrDefaultAsync(l => l.Id == linkId && l.PersonaId == id, ct);
        if (link is null)
        {
            return new PersonaEditOutcome(PersonaEditResult.NotFound);
        }

        var urlLower = link.Url.ToLowerInvariant();
        db.PersonaLinks.Remove(link);
        if (!await db.PersonaLinkExclusions.AnyAsync(e => e.PersonaId == id && e.Url.ToLower() == urlLower, ct))
        {
            db.PersonaLinkExclusions.Add(new PersonaLinkExclusion { PersonaId = id, Url = link.Url });
        }

        persona!.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("remove_persona_link", id, persona.Slug, new() { ["url"] = link.Url }, ct);
        return Ok(id, persona.Slug);
    }

    public async Task<PersonaEditOutcome> AddAccountAsync(Guid id, PersonaAccountEntry request, CancellationToken ct)
    {
        var persona = await db.Personas.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (NotLive(persona) is { } refused)
        {
            return refused;
        }

        // The operator's decision now is the latest one: the persona's own exclusion of this account
        // goes, under the Riot ID typed and the tracked account it answers to. A takedown stays.
        var platform = request.PlatformId?.Trim().ToUpperInvariant() ?? string.Empty;
        var nameLower = request.GameName?.Trim().ToLowerInvariant() ?? string.Empty;
        var tagLower = request.TagLine?.Trim().TrimStart('#').ToLowerInvariant() ?? string.Empty;
        var trackedIds = await db.RiotAccounts
            .AsNoTracking()
            .Where(a => a.PlatformId == platform && a.GameName.ToLower() == nameLower
                        && a.TagLine != null && a.TagLine.ToLower() == tagLower)
            .Select(a => (Guid?)a.Id)
            .ToListAsync(ct);
        await db.PersonaAccountExclusions
            .Where(e => e.PersonaId == id && !e.IsTakedown
                        && ((e.PlatformId == platform && e.GameName.ToLower() == nameLower && e.TagLine.ToLower() == tagLower)
                            || (e.RiotAccountId != null && trackedIds.Contains(e.RiotAccountId))))
            .ExecuteDeleteAsync(ct);

        var result = await upsertService.UpsertAsync(
            new PersonaUpsertEntry
            {
                Slug = persona!.Slug,
                DisplayName = persona.DisplayName,
                Source = ManualSource,
                Accounts = [request],
                SeedMissing = true,
            },
            ct);
        if (result.Error is { } error)
        {
            return new PersonaEditOutcome(PersonaEditResult.Conflict, error);
        }

        var account = result.Accounts.Single();
        await audit.RecordAsync(
            "add_persona_account",
            id,
            persona.Slug,
            new()
            {
                ["riotId"] = $"{account.GameName}#{account.TagLine}",
                ["platform"] = account.PlatformId,
                ["status"] = account.Status,
            },
            ct);
        return Ok(id, persona.Slug, result.Accounts);
    }

    public async Task<PersonaEditOutcome> UnlinkAccountAsync(Guid id, Guid accountId, CancellationToken ct)
    {
        var persona = await db.Personas.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (NotLive(persona) is { } refused)
        {
            return refused;
        }

        var row = await db.PersonaAccounts.FirstOrDefaultAsync(a => a.Id == accountId && a.PersonaId == id, ct);
        if (row is null)
        {
            return new PersonaEditOutcome(PersonaEditResult.NotFound);
        }

        db.PersonaAccounts.Remove(row);
        var nameLower = row.GameName.ToLowerInvariant();
        var tagLower = row.TagLine.ToLowerInvariant();
        var existing = await db.PersonaAccountExclusions.FirstOrDefaultAsync(
            e => e.PersonaId == id && e.PlatformId == row.PlatformId
                 && e.GameName.ToLower() == nameLower && e.TagLine.ToLower() == tagLower,
            ct);
        if (existing is null)
        {
            db.PersonaAccountExclusions.Add(new PersonaAccountExclusion
            {
                PersonaId = id,
                GameName = row.GameName,
                TagLine = row.TagLine,
                PlatformId = row.PlatformId,
                RiotAccountId = row.RiotAccountId,
            });
        }
        else
        {
            existing.RiotAccountId ??= row.RiotAccountId;
        }

        // Claims other personas made on it are moot: the account is free again for their sources.
        db.PersonaAccountConflicts.RemoveRange(await PersonaConflictQueries.OpenOnAccount(db, id, row).ToListAsync(ct));

        persona!.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(
            "unlink_persona_account",
            id,
            persona.Slug,
            new() { ["riotId"] = $"{row.GameName}#{row.TagLine}", ["platform"] = row.PlatformId },
            ct);
        return Ok(id, persona.Slug);
    }

    /// <summary>Refuses an edit of an unknown, merged or removed persona.</summary>
    internal static PersonaEditOutcome? NotLive(Persona? persona) => persona switch
    {
        null => new PersonaEditOutcome(PersonaEditResult.NotFound),
        { RemovedAtUtc: not null } => new PersonaEditOutcome(PersonaEditResult.Conflict, "This known player was removed."),
        { MergedIntoPersonaId: not null } => new PersonaEditOutcome(
            PersonaEditResult.Conflict, "This known player was merged into another; edit that one."),
        _ => null,
    };

    private static (PersonaLinkKind Kind, string Url)? ParseLink(PersonaLinkEntry entry) =>
        Enum.TryParse<PersonaLinkKind>(entry.Kind?.Trim(), ignoreCase: true, out var kind)
        && Enum.IsDefined(kind)
        && PersonaUpsertService.NormaliseUrl(entry.Url) is { } url
            ? (kind, url)
            : null;

    private static PersonaEditOutcome Invalid(PersonaLinkEntry link) => new(
        PersonaEditResult.Invalid,
        $"The link {link.Kind} '{link.Url}' needs a kind among {string.Join(", ", Enum.GetNames<PersonaLinkKind>())} "
        + "and an absolute http(s) URL.");

    private static PersonaEditOutcome Ok(Guid id, string slug, IReadOnlyList<PersonaAccountUpsertResult>? accounts = null) =>
        new(PersonaEditResult.Ok, Edited: new PersonaEditedResponse { Id = id, Slug = slug, Accounts = accounts ?? [] });
}
