using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Personas;

/// <summary>
/// The intake's memory of what it refused (#2253): a claim on an account another persona holds is
/// recorded for the admin's conflict queue, and a claim an operator already settled is recognised
/// so it is neither written nor raised again. Shares the intake's <see cref="TrueMainDbContext"/>,
/// so what it adds is saved with the persona, in the same <c>SaveChanges</c>.
/// </summary>
public sealed class PersonaConflictRecorder(TrueMainDbContext db)
{
    /// <summary>The Riot IDs and tracked accounts <paramref name="persona"/> may no longer claim.</summary>
    public async Task<PersonaExclusions> LoadExclusionsAsync(Persona persona, CancellationToken ct)
    {
        var rows = await db.PersonaAccountExclusions
            .AsNoTracking()
            .Where(e => e.PersonaId == persona.Id)
            .Select(e => new { e.PlatformId, e.GameName, e.TagLine, e.RiotAccountId })
            .ToListAsync(ct);

        return new PersonaExclusions(
            rows.Select(r => PersonaExclusions.Key(r.PlatformId, r.GameName, r.TagLine)).ToHashSet(StringComparer.Ordinal),
            rows.Where(r => r.RiotAccountId is not null).Select(r => r.RiotAccountId!.Value).ToHashSet());
    }

    /// <summary>
    /// Records that <paramref name="claimant"/>'s source lists an account <paramref name="ownerPersonaId"/>
    /// holds: a new open conflict, or one more sighting of the same claim.
    /// </summary>
    public async Task RecordAsync(
        Persona claimant,
        Guid ownerPersonaId,
        string gameName,
        string tagLine,
        string platformId,
        Guid? riotAccountId,
        PersonaSource source,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var conflict = FindLocal(claimant.Id, platformId, gameName, tagLine)
                       ?? await FindStoredAsync(claimant.Id, platformId, gameName, tagLine, ct);
        if (conflict is null)
        {
            db.PersonaAccountConflicts.Add(new PersonaAccountConflict
            {
                Claimant = claimant,
                OwnerPersonaId = ownerPersonaId,
                GameName = gameName,
                TagLine = tagLine,
                PlatformId = platformId,
                RiotAccountId = riotAccountId,
                Source = source,
                Status = PersonaConflictStatus.Open,
                FirstSeenAtUtc = now,
                LastSeenAtUtc = now,
                TimesSeen = 1,
            });
            return;
        }

        conflict.OwnerPersonaId = ownerPersonaId;
        conflict.RiotAccountId = riotAccountId ?? conflict.RiotAccountId;
        conflict.Source = source;
        conflict.LastSeenAtUtc = now;
        conflict.TimesSeen++;
        if (conflict.Status == PersonaConflictStatus.Resolved)
        {
            // Settled, yet raised again by the same persona without an exclusion: the account changed
            // hands since (another resolution moved it), so it is a new decision to take.
            conflict.Status = PersonaConflictStatus.Open;
            conflict.Resolution = null;
            conflict.ResolvedAtUtc = null;
        }
    }

    private PersonaAccountConflict? FindLocal(Guid claimantId, string platformId, string gameName, string tagLine) =>
        db.PersonaAccountConflicts.Local.FirstOrDefault(c =>
            (c.ClaimantPersonaId == claimantId || c.Claimant?.Id == claimantId)
            && c.PlatformId == platformId
            && string.Equals(c.GameName, gameName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.TagLine, tagLine, StringComparison.OrdinalIgnoreCase));

    private Task<PersonaAccountConflict?> FindStoredAsync(
        Guid claimantId, string platformId, string gameName, string tagLine, CancellationToken ct)
    {
        var nameLower = gameName.ToLowerInvariant();
        var tagLower = tagLine.ToLowerInvariant();
        return db.PersonaAccountConflicts.FirstOrDefaultAsync(
            c => c.ClaimantPersonaId == claimantId
                 && c.PlatformId == platformId
                 && c.GameName.ToLower() == nameLower
                 && c.TagLine.ToLower() == tagLower,
            ct);
    }
}

/// <summary>What one persona may no longer claim: Riot IDs (case-insensitive) and tracked accounts.</summary>
public sealed class PersonaExclusions(HashSet<string> riotIds, HashSet<Guid> accountIds)
{
    public static readonly PersonaExclusions None = new([], []);

    public bool Excludes(string platformId, string gameName, string tagLine) =>
        riotIds.Contains(Key(platformId, gameName, tagLine));

    public bool Excludes(Guid riotAccountId) => accountIds.Contains(riotAccountId);

    internal static string Key(string platformId, string gameName, string tagLine) =>
        $"{platformId}\u001f{gameName.ToLowerInvariant()}\u001f{tagLine.ToLowerInvariant()}";
}
