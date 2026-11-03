using Data;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Personas;

/// <summary>
/// The rules of the bulk resolution of the most certain conflicts (#2295), shared by its preview
/// and its write so the two can never disagree. A conflict qualifies when its two known players list
/// at least one social channel in common, none that differs, and its score reaches the operator's
/// floor; it is merged into the older of the two — the one whose slug the site has advertised the
/// longest, as the same-name merge does (#2271).
/// </summary>
public static class PersonaConflictBulk
{
    /// <summary>Conflicts per bulk action: each one is a merge, and the operator reads the whole preview.</summary>
    public const int BatchLimit = 100;

    /// <summary>The lowest floor accepted: one shared social channel.</summary>
    public const int MinimumFloor = PersonaConflictConfidence.SharedSocialWeight;

    public static bool IsEligible(PersonaConflictEvidence evidence, int floor) =>
        evidence.DifferingSocial.Count == 0 && evidence.SharedSocial.Count > 0 && evidence.Score >= floor;

    /// <summary>True when the claimant is the older persona; ties broken on the id, as the same-name merge does.</summary>
    public static bool KeepClaimant(DateTime claimantCreatedAtUtc, Guid claimantId, DateTime ownerCreatedAtUtc, Guid ownerId) =>
        claimantCreatedAtUtc != ownerCreatedAtUtc
            ? claimantCreatedAtUtc < ownerCreatedAtUtc
            : string.CompareOrdinal(claimantId.ToString("D"), ownerId.ToString("D")) <= 0;

    /// <summary>What the two personas of each conflict share, from their links (<see cref="PersonaConflictConfidence"/>).</summary>
    public static async Task<Dictionary<Guid, PersonaConflictEvidence>> EvidenceAsync(
        TrueMainDbContext db, IEnumerable<(Guid Id, Guid ClaimantId, Guid OwnerId)> conflicts, CancellationToken ct)
    {
        var pairs = conflicts.ToList();
        var personaIds = pairs.SelectMany(c => new[] { c.ClaimantId, c.OwnerId }).Distinct().ToList();
        var links = (await db.PersonaLinks
                .AsNoTracking()
                .Where(l => personaIds.Contains(l.PersonaId))
                .Select(l => new { l.PersonaId, l.Kind, l.Url })
                .ToListAsync(ct))
            .ToLookup(l => l.PersonaId, l => (l.Kind, l.Url));
        return pairs.ToDictionary(
            c => c.Id,
            c => PersonaConflictConfidence.Evaluate(links[c.ClaimantId].ToList(), links[c.OwnerId].ToList()));
    }
}
