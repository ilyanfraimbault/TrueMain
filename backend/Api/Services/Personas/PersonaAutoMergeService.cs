using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;
using TrueMain.Requests.Ops;

namespace TrueMain.Services.Personas;

public interface IPersonaAutoMergeService
{
    /// <summary>
    /// Settles as merges the open conflicts whose two personas are homonyms, oldest claim first, up
    /// to <see cref="PersonaAutoMergeService.SweepLimit"/> per call; then merges the homonyms sharing
    /// a social channel (#2286).
    /// </summary>
    Task<PersonaAutoMergeResponse> MergeHomonymConflictsAsync(CancellationToken ct);
}

/// <summary>
/// The rule the operator gave the conflict queue (#2271): two known players with the same name
/// (<see cref="PersonaHomonyms"/>) that claim the same Riot account are one person, published twice
/// by two sources under two slugs. Such a conflict needs no decision, so it is merged without one —
/// into the older persona, whose slug the site has advertised the longest — through the conflict
/// resolution itself, audited as <c>auto</c>. The same holds for two of them listing one social
/// channel (#2286): a channel conflict between homonyms is merged the same way, through the homonyms
/// merge. The intake sweeps after every batch, the API once at startup; the admin can sweep on demand.
/// </summary>
public sealed class PersonaAutoMergeService(
    TrueMainDbContext db,
    IPersonaConflictResolutionService resolution,
    PersonaChannelConflictFinder channelConflicts,
    IPersonaHomonymService homonyms,
    ILogger<PersonaAutoMergeService> logger) : IPersonaAutoMergeService
{
    /// <summary>A sweep's bound: a merge is a handful of statements, and the intake waits on it.</summary>
    public const int SweepLimit = 500;

    public async Task<PersonaAutoMergeResponse> MergeHomonymConflictsAsync(CancellationToken ct)
    {
        var refused = new List<Guid>();
        var merged = 0;
        var alsoResolved = 0;
        while (merged + refused.Count < SweepLimit && await NextAsync(refused, ct) is { } next)
        {
            var outcome = await resolution.ResolveAsync(
                next.Id,
                new PersonaConflictResolveRequest { Action = "merge", Keep = next.KeepClaimant ? "claimant" : "owner" },
                "auto",
                ct);
            if (outcome.Resolved is { } resolved)
            {
                merged++;
                alsoResolved += resolved.AlsoResolved;
            }
            else
            {
                // Changed under the sweep (a concurrent operator action): left for the queue.
                refused.Add(next.Id);
            }
        }

        if (merged > 0)
        {
            logger.LogInformation(
                "Merged {Merged} same-name persona conflict(s) automatically ({AlsoResolved} more settled by them).",
                merged,
                alsoResolved);
        }

        var (channelMerged, channelResolved, channelSkipped) = await MergeSameNameChannelsAsync(ct);
        return new PersonaAutoMergeResponse
        {
            Merged = merged,
            MergedByChannel = channelMerged,
            AlsoResolved = alsoResolved + channelResolved,
            Skipped = refused.Count + channelSkipped,
        };
    }

    /// <summary>
    /// The homonyms linked by shared channels, grouped (A and B share a Twitch, B and C a YouTube: one
    /// person), each group merged into its oldest persona. A group holding a pair the operator marked
    /// as different people is theirs to split, so it is left in the queue.
    /// </summary>
    private async Task<(int Merged, int AlsoResolved, int Skipped)> MergeSameNameChannelsAsync(CancellationToken ct)
    {
        var conflicts = (await channelConflicts.FindAsync(ct)).Where(c => c.SameName).ToList();
        if (conflicts.Count == 0)
        {
            return (0, 0, 0);
        }

        var groups = Group(conflicts);
        var ids = groups.SelectMany(g => g).ToList();
        var marked = await db.PersonaDistinctPairs
            .AsNoTracking()
            .Where(d => ids.Contains(d.PersonaId) && ids.Contains(d.OtherPersonaId))
            .Select(d => new { d.PersonaId, d.OtherPersonaId })
            .ToListAsync(ct);

        int merged = 0, alsoResolved = 0, skipped = 0;
        foreach (var group in groups.Take(SweepLimit))
        {
            if (marked.Any(d => group.Contains(d.PersonaId) && group.Contains(d.OtherPersonaId)))
            {
                skipped++;
                continue;
            }

            var request = new PersonaHomonymMergeRequest { KeepId = group[0], MergeIds = group.Skip(1).ToList() };
            var outcome = await homonyms.MergeAsync(request, "auto", ct);
            if (outcome.Response is { } response)
            {
                merged += response.MergedSlugs.Count;
                alsoResolved += response.AlsoResolved;
            }
            else
            {
                // One of them was merged or removed under the sweep: the next one sees the new state.
                skipped++;
            }
        }

        if (merged > 0)
        {
            logger.LogInformation(
                "Merged {Merged} same-name persona(s) sharing a social channel automatically ({AlsoResolved} conflicts settled by them).",
                merged,
                alsoResolved);
        }

        return (merged, alsoResolved, skipped);
    }

    /// <summary>The connected groups of <paramref name="conflicts"/>, each oldest persona first.</summary>
    private static List<List<Guid>> Group(List<PersonaChannelConflict> conflicts)
    {
        var sides = conflicts.SelectMany(c => new[] { c.Older, c.Newer }).DistinctBy(s => s.Id).ToDictionary(s => s.Id);
        var parent = sides.Keys.ToDictionary(id => id);
        Guid Root(Guid id)
        {
            while (parent[id] != id)
            {
                id = parent[id] = parent[parent[id]];
            }

            return id;
        }

        foreach (var conflict in conflicts)
        {
            parent[Root(conflict.Newer.Id)] = Root(conflict.Older.Id);
        }

        return sides.Values
            .GroupBy(s => Root(s.Id))
            .Select(g => g.OrderBy(s => s.CreatedAtUtc).ThenBy(s => s.Id.ToString("D"), StringComparer.Ordinal).Select(s => s.Id).ToList())
            .ToList();
    }

    private async Task<Candidate?> NextAsync(List<Guid> refused, CancellationToken ct)
    {
        var sql = $$"""
            SELECT c."Id", (cl."CreatedAtUtc", cl."Id") <= (o."CreatedAtUtc", o."Id") AS "KeepClaimant"
            FROM persona_account_conflicts c
            JOIN personas cl ON cl."Id" = c."ClaimantPersonaId"
            JOIN personas o ON o."Id" = c."OwnerPersonaId"
            WHERE c."Status" = 'Open'
              AND cl."MergedIntoPersonaId" IS NULL AND cl."RemovedAtUtc" IS NULL
              AND o."MergedIntoPersonaId" IS NULL AND o."RemovedAtUtc" IS NULL
              AND {{PersonaHomonyms.NameKeySql("cl")}} = {{PersonaHomonyms.NameKeySql("o")}}
              AND {{PersonaHomonyms.NameKeySql("cl")}} <> ''
              AND NOT (c."Id" = ANY({0}))
            ORDER BY c."FirstSeenAtUtc", c."Id"
            LIMIT 1
            """;
        db.ChangeTracker.Clear();
        return await db.Database.SqlQueryRaw<Candidate>(sql, refused.ToArray()).FirstOrDefaultAsync(ct);
    }

    private sealed record Candidate(Guid Id, bool KeepClaimant);
}
