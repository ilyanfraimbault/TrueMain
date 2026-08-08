using Data.Configuration;
using Data.Logging.Mongo;
using MongoDB.Driver;

namespace Data.Ops.Mongo;

/// <summary>
/// Mongo adapter for the published effective-configuration snapshots (#1034), modelled on
/// <see cref="Data.Metrics.Mongo.DbStorageSnapshotStore"/>: a direct-call store with no channel
/// and no sink, because there is exactly one write per process boot.
/// </summary>
internal sealed class EffectiveConfigurationStore(MongoLogContext context) : IEffectiveConfigurationStore
{
    // Same bootstrap as DbStorageSnapshotStore: this store has no sink to hang index creation
    // off, so it ensures them on first write. The flag is set only after a success, so a
    // transient Mongo failure retries on the next boot rather than leaving the collection
    // unindexed forever.
    private int _indexesEnsured;

    public async Task<bool> UpsertAsync(EffectiveConfigurationSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!context.IsActive)
        {
            return false;
        }

        if (Volatile.Read(ref _indexesEnsured) == 0)
        {
            await context.EnsureEffectiveConfigurationIndexesAsync(ct);
            Volatile.Write(ref _indexesEnsured, 1);
        }

        var filter = Builders<EffectiveConfigurationDocument>.Filter
            .Eq(doc => doc.ProcessName, snapshot.ProcessName);

        // $set, not $push: a snapshot is an absolute reading of one process, and the most
        // recent boot is the one that should win. ProcessName comes from the filter on insert.
        var update = Builders<EffectiveConfigurationDocument>.Update
            .Set(doc => doc.Environment, snapshot.Environment)
            .Set(doc => doc.Version, snapshot.Version)
            .Set(doc => doc.CapturedAtUtc, snapshot.CapturedAtUtc)
            .Set(doc => doc.Sections, snapshot.Sections.Select(ToDocument).ToList());

        await context.EffectiveConfigurations.UpdateOneAsync(
            filter, update, new UpdateOptions { IsUpsert = true }, ct);

        return true;
    }

    public async Task<IReadOnlyList<EffectiveConfigurationSnapshot>> GetAllAsync(CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return [];
        }

        var documents = await context.EffectiveConfigurations
            .Find(Builders<EffectiveConfigurationDocument>.Filter.Empty)
            .SortBy(doc => doc.ProcessName)
            .ToListAsync(ct);

        return documents.Select(ToSnapshot).ToList();
    }

    private static EffectiveConfigurationSectionDocument ToDocument(EffectiveConfigurationSection section) =>
        new()
        {
            Name = section.Name,
            Title = section.Title,
            Description = section.Description,
            Values = section.Values.Select(value => new EffectiveConfigurationValueDocument
            {
                Key = value.Key,
                Name = value.Name,
                Value = value.Value,
                ValueLabel = value.ValueLabel,
                Origin = value.Origin,
                Source = value.Source,
                Unit = value.Unit,
                Notice = value.Notice
            }).ToList()
        };

    private static EffectiveConfigurationSnapshot ToSnapshot(EffectiveConfigurationDocument document) =>
        new()
        {
            ProcessName = document.ProcessName,
            Environment = document.Environment,
            Version = document.Version,
            CapturedAtUtc = DateTime.SpecifyKind(document.CapturedAtUtc, DateTimeKind.Utc),
            Sections = document.Sections.Select(section => new EffectiveConfigurationSection
            {
                Name = section.Name,
                Title = section.Title,
                Description = section.Description,
                Values = section.Values.Select(value => new EffectiveConfigurationValue
                {
                    Key = value.Key,
                    Name = value.Name,
                    Value = value.Value,
                    ValueLabel = value.ValueLabel,
                    Origin = value.Origin,
                    Source = value.Source,
                    Unit = value.Unit,
                    Notice = value.Notice
                }).ToList()
            }).ToList()
        };
}
