using Data.Logging.Mongo;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Data.Desktop;

/// <summary>
/// Mongo adapter for the desktop app's telemetry (#1805). A direct-call store, like the
/// snapshot stores: one write per request, with no channel to drain — the app sends a batch
/// every few minutes and the site one row per download, nothing that needs smoothing.
/// </summary>
/// <remarks>
/// It owns its two collections rather than adding them to <see cref="MongoLogContext"/>, and
/// reaches them through the context's database: same client, same database, and the context
/// stays the holder of the collections the logging pipeline itself writes.
/// </remarks>
internal sealed class DesktopTelemetryStore(MongoLogContext context, IOptions<MongoLoggingOptions> options)
    : IDesktopTelemetryStore
{
    private const int DuplicateKeyCode = 11000;

    private readonly MongoLoggingOptions _options = options.Value;

    // Ensured on first use and flagged only after a success, as in DbStorageSnapshotStore:
    // a transient failure retries on the next request instead of leaving the collection
    // unindexed, and two first requests both ensuring is harmless.
    private int _indexesEnsured;

    private IMongoCollection<DesktopUsageDayDocument> Usage =>
        field ??= context.Database.GetCollection<DesktopUsageDayDocument>(_options.DesktopUsageCollection);

    private IMongoCollection<DesktopDownloadDayDocument> Downloads =>
        field ??= context.Database.GetCollection<DesktopDownloadDayDocument>(_options.DesktopDownloadsCollection);

    public async Task<bool> RecordUsageAsync(DesktopUsageBatch batch, DateTime receivedAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (!context.IsActive)
        {
            return false;
        }

        await EnsureIndexesAsync(ct);

        var day = Day(receivedAtUtc);
        var filter = Builders<DesktopUsageDayDocument>.Filter.And(
            Builders<DesktopUsageDayDocument>.Filter.Eq(doc => doc.DayUtc, day),
            Builders<DesktopUsageDayDocument>.Filter.Eq(doc => doc.InstallId, batch.InstallId.ToString("D")));

        var set = Builders<DesktopUsageDayDocument>.Update;
        var updates = new List<UpdateDefinition<DesktopUsageDayDocument>>
        {
            set.Set(doc => doc.AppVersion, batch.AppVersion),
            set.Set(doc => doc.Os, batch.Os),
            set.Set(doc => doc.LastSeenUtc, receivedAtUtc),
            set.SetOnInsert(doc => doc.FirstSeenUtc, receivedAtUtc),
            set.Inc(doc => doc.Launches, batch.Launches),
            set.Inc(doc => doc.OpenMinutes, batch.OpenMinutes)
        };

        // Only ever raised: a later batch of the same day without the flag must not clear it.
        updates.Add(batch.FirstLaunch
            ? set.Set(doc => doc.FirstLaunch, true)
            : set.SetOnInsert(doc => doc.FirstLaunch, false));

        // Each key is its own field under the map, so two batches add up per page rather than
        // the second replacing the first. The keys are allow-listed upstream; the guard here
        // only keeps a field path from ever being built out of anything but letters.
        foreach (var (page, views) in batch.PageViews)
        {
            if (IsFieldName(page) && views > 0)
            {
                updates.Add(set.Inc<int>($"pageViews.{page}", views));
            }
        }

        foreach (var (feature, uses) in batch.Features)
        {
            if (IsFieldName(feature) && uses > 0)
            {
                updates.Add(set.Inc<int>($"features.{feature}", uses));
            }
        }

        await UpsertAsync(Usage, filter, set.Combine(updates), ct);
        return true;
    }

    public async Task<bool> RecordDownloadAsync(string platform, string version, DateTime atUtc, CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return false;
        }

        await EnsureIndexesAsync(ct);

        var filter = Builders<DesktopDownloadDayDocument>.Filter.And(
            Builders<DesktopDownloadDayDocument>.Filter.Eq(doc => doc.DayUtc, Day(atUtc)),
            Builders<DesktopDownloadDayDocument>.Filter.Eq(doc => doc.Platform, platform),
            Builders<DesktopDownloadDayDocument>.Filter.Eq(doc => doc.Version, version));

        await UpsertAsync(
            Downloads, filter, Builders<DesktopDownloadDayDocument>.Update.Inc(doc => doc.Count, 1L), ct);
        return true;
    }

    public async Task<IReadOnlyList<DesktopUsageDay>> GetUsageAsync(DateTime sinceDayUtc, CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return [];
        }

        var documents = await Usage
            .Find(Builders<DesktopUsageDayDocument>.Filter.Gte(doc => doc.DayUtc, Day(sinceDayUtc)))
            .SortBy(doc => doc.DayUtc)
            .ToListAsync(ct);

        return documents
            .Select(doc => new DesktopUsageDay(
                Utc(doc.DayUtc),
                doc.InstallId,
                doc.AppVersion,
                doc.Os,
                doc.FirstLaunch,
                doc.Launches,
                doc.OpenMinutes,
                doc.PageViews,
                doc.Features))
            .ToList();
    }

    public async Task<IReadOnlyList<DesktopDownloadDay>> GetDownloadsAsync(DateTime sinceDayUtc, CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return [];
        }

        var documents = await Downloads
            .Find(Builders<DesktopDownloadDayDocument>.Filter.Gte(doc => doc.DayUtc, Day(sinceDayUtc)))
            .SortBy(doc => doc.DayUtc)
            .ToListAsync(ct);

        return documents
            .Select(doc => new DesktopDownloadDay(Utc(doc.DayUtc), doc.Platform, doc.Version, doc.Count))
            .ToList();
    }

    /// <summary>
    /// An upsert, retried once on a duplicate key: two requests for a document that does not
    /// exist yet can both try to insert it, and the unique index lets one through. The retry
    /// then finds that document and updates it, so neither batch is lost.
    /// </summary>
    private static async Task UpsertAsync<TDoc>(
        IMongoCollection<TDoc> collection,
        FilterDefinition<TDoc> filter,
        UpdateDefinition<TDoc> update,
        CancellationToken ct)
    {
        var upsert = new UpdateOptions { IsUpsert = true };
        try
        {
            await collection.UpdateOneAsync(filter, update, upsert, ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == DuplicateKeyCode)
        {
            await collection.UpdateOneAsync(filter, update, upsert, ct);
        }
    }

    private async Task EnsureIndexesAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _indexesEnsured) != 0)
        {
            return;
        }

        // The upsert key, unique so a race cannot split an install's day into two documents
        // that the read would then count as two active installs. Its day prefix also serves
        // the window scan every read performs.
        await Usage.Indexes.CreateOneAsync(
            new CreateIndexModel<DesktopUsageDayDocument>(
                Builders<DesktopUsageDayDocument>.IndexKeys
                    .Ascending(doc => doc.DayUtc)
                    .Ascending(doc => doc.InstallId),
                new CreateIndexOptions { Name = "ux_day_install", Unique = true }),
            cancellationToken: ct);
        await MongoIndexes.ReconcileTtlAsync(Usage, doc => doc.DayUtc, _options.DesktopUsageRetention, ct);

        await Downloads.Indexes.CreateOneAsync(
            new CreateIndexModel<DesktopDownloadDayDocument>(
                Builders<DesktopDownloadDayDocument>.IndexKeys
                    .Ascending(doc => doc.DayUtc)
                    .Ascending(doc => doc.Platform)
                    .Ascending(doc => doc.Version),
                new CreateIndexOptions { Name = "ux_day_platform_version", Unique = true }),
            cancellationToken: ct);
        await MongoIndexes.ReconcileTtlAsync(Downloads, doc => doc.DayUtc, _options.DesktopDownloadsRetention, ct);

        Volatile.Write(ref _indexesEnsured, 1);
    }

    private static bool IsFieldName(string key) =>
        key.Length is > 0 and <= 32 && key.All(char.IsAsciiLetter);

    private static DateTime Day(DateTime instantUtc) =>
        DateTime.SpecifyKind(instantUtc.Date, DateTimeKind.Utc);

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
