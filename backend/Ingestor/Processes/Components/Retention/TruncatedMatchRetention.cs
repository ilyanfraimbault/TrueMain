using Core.Lol.Patches;
using Data;
using Data.Aggregation;
using Data.BuildFacts;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.Retention;

/// <summary>
/// The truncated half of match retention (#2368, #2358): what is kept of a ranked match once
/// its full rows go, and how long.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written in the deleting transaction.</b> <see cref="WriteAsync"/> runs inside each
/// batch's transaction, before the match's rows are deleted, so a match is either still whole
/// or already truncated — never lost between the two. A match can still come back: ingested
/// again after its truncation, it has raw rows and seats at once until retention deletes it
/// again, and it keeps the seats it has (#2449).
/// </para>
/// <para>
/// <b>Two tiers, both bounded.</b> Every seat of the newest <c>TruncatedRetainedPatchCount</c>
/// patches stays, which is what widens matchup and draft samples; older than that, only tracked
/// accounts' seats stay, up to <c>TruncatedTrackedRetainedPatchCount</c> patches, for their
/// profiles. A patch count rather than a season: 16.x runs a whole year, about 24 patches,
/// which measured close to the disk's headroom.
/// </para>
/// </remarks>
public static class TruncatedMatchRetention
{
    /// <summary>
    /// Truncates the games of <paramref name="queueId"/> among <paramref name="matchIds"/> into
    /// <c>truncated_match_seats</c>; remakes are left out, as every build read leaves them out.
    /// Returns the seats written.
    /// </summary>
    public static async Task<int> WriteAsync(
        TrueMainDbContext db,
        IReadOnlyCollection<string> matchIds,
        int queueId,
        Func<string, CancellationToken, Task<IReadOnlyDictionary<int, ItemMetadata>?>> itemMetadataFor,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(matchIds);
        ArgumentNullException.ThrowIfNull(itemMetadataFor);

        var matches = await db.Matches
            .AsNoTracking()
            .Where(match => matchIds.Contains(match.Id)
                && match.QueueId == queueId
                && match.GameDurationSeconds >= ChampionCohort.MinimumGameDurationSeconds
                && !match.EndedInEarlySurrender
                && !db.TruncatedMatchSeats.Any(seat => seat.MatchId == match.Id))
            .ToListAsync(ct);
        if (matches.Count == 0)
        {
            return 0;
        }

        var kept = matches.Select(match => match.Id).ToList();
        var participants = await db.MatchParticipants
            .AsNoTracking()
            .Where(participant => kept.Contains(participant.MatchId))
            .ToListAsync(ct);

        var perks = (await db.ParticipantPerkSelections
                .AsNoTracking()
                .Where(selection => kept.Contains(selection.MatchId))
                .Join(
                    db.PerkSelectionCatalogs.AsNoTracking(),
                    selection => selection.PerkSelectionCatalogId,
                    catalog => catalog.Id,
                    (selection, catalog) => new
                    {
                        selection.MatchId,
                        selection.ParticipantId,
                        Perk = new TruncatedPerkSelection(catalog.StyleDescription, catalog.SelectionIndex, catalog.PerkId),
                    })
                .ToListAsync(ct))
            .ToLookup(row => (row.MatchId, row.ParticipantId), row => row.Perk);

        var metadataByVersion = new Dictionary<string, IReadOnlyDictionary<int, ItemMetadata>?>(StringComparer.Ordinal);
        foreach (var version in matches.Select(match => match.GameVersion).Distinct(StringComparer.Ordinal))
        {
            metadataByVersion[version] = await itemMetadataFor(version, ct);
        }

        var matchById = matches.ToDictionary(match => match.Id, StringComparer.Ordinal);
        var seats = participants
            .Select(participant =>
            {
                var match = matchById[participant.MatchId];
                return TruncatedSeatBuilder.Build(
                    match,
                    participant,
                    [.. perks[(participant.MatchId, participant.ParticipantId)]],
                    metadataByVersion[match.GameVersion]);
            })
            .ToList();

        db.TruncatedMatchSeats.AddRange(seats);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return seats.Count;
    }

    /// <summary>
    /// The item data of a truncated match's patch; null when it is unavailable, so its seats
    /// keep every fact but the items rather than failing the batch.
    /// </summary>
    public static Func<string, CancellationToken, Task<IReadOnlyDictionary<int, ItemMetadata>?>> ItemMetadataReader(
        IItemMetadataProvider provider,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(logger);

        return async (gameVersion, ct) =>
        {
            try
            {
                return await provider.GetItemsAsync(gameVersion, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Item metadata unavailable for {GameVersion}; truncated seats keep no item facts.", gameVersion);
                return null;
            }
        };
    }

    /// <summary>
    /// Drops what the two tiers no longer keep: untracked seats outside the newest
    /// <paramref name="retainedPatchCount"/> patches, and every seat outside the newest
    /// <paramref name="trackedRetainedPatchCount"/>. Nothing when truncation is off. Returns the
    /// seats deleted.
    /// </summary>
    public static async Task<int> PurgeAsync(
        IDbContextFactory<TrueMainDbContext> dbContextFactory,
        int retainedPatchCount,
        int trackedRetainedPatchCount,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dbContextFactory);
        if (retainedPatchCount <= 0)
        {
            return 0;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var patches = await db.TruncatedMatchSeats.AsNoTracking().Select(seat => seat.Patch).Distinct().ToListAsync(ct);
        var (outsideWindow, expired) = Tiers(patches, retainedPatchCount, trackedRetainedPatchCount);
        var deleted = 0;

        if (expired.Count > 0)
        {
            deleted += await db.TruncatedMatchSeats
                .Where(seat => expired.Contains(seat.Patch))
                .ExecuteDeleteAsync(ct);
        }

        if (outsideWindow.Count > 0)
        {
            deleted += await db.TruncatedMatchSeats
                .Where(seat => outsideWindow.Contains(seat.Patch) && seat.RiotAccountId == null)
                .ExecuteDeleteAsync(ct);
        }

        return deleted;
    }

    /// <summary>
    /// Splits the stored patches into those outside the full window (untracked seats go) and
    /// those outside the tracked window (every seat goes). Ordered on parsed versions —
    /// <c>16.9</c> sorts above <c>16.19</c> as a string — and an unparseable patch counts as
    /// the oldest and is expired outright. A tracked window narrower than the full one is
    /// read as the full one.
    /// </summary>
    public static (List<string> OutsideWindow, List<string> Expired) Tiers(
        IReadOnlyCollection<string> patches,
        int retainedPatchCount,
        int trackedRetainedPatchCount)
    {
        ArgumentNullException.ThrowIfNull(patches);

        var parsed = patches
            .Select(raw => (Raw: raw, Version: PatchVersion.TryParse(raw, out var version) ? version : (PatchVersion?)null))
            .ToList();
        var ordered = parsed
            .Where(entry => entry.Version.HasValue)
            .OrderByDescending(entry => entry.Version)
            .Select(entry => entry.Raw)
            .ToList();
        var unparseable = parsed.Where(entry => !entry.Version.HasValue).Select(entry => entry.Raw).ToList();

        var full = Math.Max(1, retainedPatchCount);
        var tracked = Math.Max(full, trackedRetainedPatchCount);
        return ([.. ordered.Skip(full)], [.. ordered.Skip(tracked), .. unparseable]);
    }
}
