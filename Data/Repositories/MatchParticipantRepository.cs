using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class MatchParticipantRepository(TrueMainDbContext db) : IMatchParticipantRepository
{
    public Task<List<MatchParticipant>> GetByMatchIdAsync(string matchId, CancellationToken ct)
        => db.MatchParticipants.Where(p => p.MatchId == matchId).ToListAsync(ct);

    public Task<List<ParticipantRow>> GetRecentParticipantsAsync(string platformId, string puuid, int queueId, int take, CancellationToken ct)
    {
        return (
                from participant in db.MatchParticipants.AsNoTracking()
                join match in db.Matches.AsNoTracking() on participant.MatchId equals match.Id
                where participant.Puuid == puuid &&
                      match.PlatformId == platformId &&
                      match.QueueId == queueId
                orderby match.GameStartTimeUtc descending
                select new ParticipantRow(participant.ChampionId, participant.TeamPosition)
            )
            .Take(Math.Max(1, take))
            .ToListAsync(ct);
    }

    public void AddRange(IEnumerable<MatchParticipant> participants)
        => db.MatchParticipants.AddRange(participants);

    public async Task<Dictionary<PerkCatalogKey, int>> GetOrCreatePerkCatalogIdsAsync(
        IReadOnlyCollection<PerkCatalogKey> keys,
        CancellationToken ct)
    {
        var distinctKeys = keys
            .Distinct()
            .ToArray();

        if (distinctKeys.Length == 0)
        {
            return [];
        }

        var existingMap = await LoadCatalogIdsByKeysAsync(distinctKeys, ct);
        var missingKeys = distinctKeys
            .Where(key => !existingMap.ContainsKey(key))
            .ToArray();

        if (missingKeys.Length == 0)
        {
            return existingMap;
        }

        db.PerkSelectionCatalogs.AddRange(
            missingKeys.Select(key => new PerkSelectionCatalog
            {
                StyleId = key.StyleId,
                SelectionIndex = key.SelectionIndex,
                PerkId = key.PerkId,
                StyleDescription = key.StyleDescription
            }));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Unique index collisions can occur under concurrent ingestion.
            db.ChangeTracker.Clear();
        }

        return await LoadCatalogIdsByKeysAsync(distinctKeys, ct);
    }

    public void AddPerkSelections(IEnumerable<ParticipantPerkSelection> selections)
        => db.ParticipantPerkSelections.AddRange(selections);

    private async Task<Dictionary<PerkCatalogKey, int>> LoadCatalogIdsByKeysAsync(
        IReadOnlyCollection<PerkCatalogKey> keys,
        CancellationToken ct)
    {
        var map = new Dictionary<PerkCatalogKey, int>();
        var styleIds = keys.Select(key => key.StyleId).Distinct().ToArray();

        var catalogs = await db.PerkSelectionCatalogs
            .AsNoTracking()
            .Where(catalog => styleIds.Contains(catalog.StyleId))
            .ToListAsync(ct);

        foreach (var catalog in catalogs)
        {
            var key = new PerkCatalogKey(
                catalog.StyleId,
                catalog.SelectionIndex,
                catalog.PerkId,
                catalog.StyleDescription);
            map.TryAdd(key, catalog.Id);
        }

        return keys
            .Where(key => map.ContainsKey(key))
            .ToDictionary(key => key, key => map[key]);
    }
}
