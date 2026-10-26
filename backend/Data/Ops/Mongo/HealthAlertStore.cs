using Data.Logging.Mongo;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Data.Ops.Mongo;

/// <summary>
/// Mongo adapter for the health alerter (#2292). Owns its two collections rather than adding
/// them to <see cref="MongoLogContext"/>, reaching them through the context's database: same
/// client, same database.
/// </summary>
internal sealed class HealthAlertStore(MongoLogContext context, IOptions<MongoLoggingOptions> options)
    : IHealthAlertStore
{
    private const int DuplicateKeyCode = 11000;

    private readonly MongoLoggingOptions _options = options.Value;

    private int _indexesEnsured;

    public bool IsActive => context.IsActive;

    private IMongoCollection<HealthAlertStateDocument> States =>
        field ??= context.Database.GetCollection<HealthAlertStateDocument>(_options.HealthAlertStateCollection);

    private IMongoCollection<HealthAlertDocument> Alerts =>
        field ??= context.Database.GetCollection<HealthAlertDocument>(_options.HealthAlertsCollection);

    public async Task<HealthAlertStateDocument?> GetStateAsync(CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return null;
        }

        return await States
            .Find(doc => doc.Id == HealthAlertStateDocument.SingletonId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> TrySaveStateAsync(HealthAlertStateDocument state, long expectedVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!context.IsActive)
        {
            state.Version = expectedVersion + 1;
            return true;
        }

        state.Id = HealthAlertStateDocument.SingletonId;
        state.Version = expectedVersion + 1;
        try
        {
            // Upsert on (id, expected version): a stored document with another version does
            // not match, the upsert then inserts a second document under the same id, and the
            // unique _id refuses it — the lost race surfaces as a duplicate key.
            var result = await States.ReplaceOneAsync(
                doc => doc.Id == HealthAlertStateDocument.SingletonId && doc.Version == expectedVersion,
                state,
                new ReplaceOptions { IsUpsert = true },
                ct);
            return result.IsAcknowledged;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == DuplicateKeyCode)
        {
            state.Version = expectedVersion;
            return false;
        }
    }

    public async Task RecordAlertAsync(HealthAlertDocument alert, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(alert);

        if (!context.IsActive)
        {
            return;
        }

        await EnsureIndexesAsync(ct);
        await Alerts.InsertOneAsync(alert, options: null, ct);
    }

    public async Task<IReadOnlyList<HealthAlertDocument>> GetRecentAlertsAsync(int limit, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        if (!context.IsActive)
        {
            return [];
        }

        return await Alerts
            .Find(FilterDefinition<HealthAlertDocument>.Empty)
            .SortByDescending(doc => doc.SentAtUtc)
            .ThenByDescending(doc => doc.Id)
            .Limit(limit)
            .ToListAsync(ct);
    }

    private async Task EnsureIndexesAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _indexesEnsured) != 0)
        {
            return;
        }

        // The newest-first read leads on sentAtUtc, which the TTL index covers.
        await MongoIndexes.ReconcileTtlAsync(Alerts, doc => doc.SentAtUtc, _options.HealthAlertsRetention, ct);
        Volatile.Write(ref _indexesEnsured, 1);
    }
}
