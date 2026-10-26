using Data.Logging.Mongo;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Data.Ops.Mongo;

/// <summary>
/// Mongo adapter for the process boot log (#2297). Direct-call, one write per boot, and it owns
/// its collection rather than adding it to <see cref="MongoLogContext"/> — the
/// <see cref="Data.Metrics.Mongo.RequestLatencyStore"/> pattern.
/// </summary>
internal sealed class ProcessBootStore(MongoLogContext context, IOptions<MongoLoggingOptions> options)
    : IProcessBootStore
{
    private const string RestartsTtlIndexName = "ttl_restarts";

    // IndexOptionsConflict / IndexKeySpecsConflict: the TTL index exists with another expiry.
    private static readonly int[] IndexConflictCodes = [85, 86];

    private static readonly FilterDefinitionBuilder<ProcessBootDocument> Filter = Builders<ProcessBootDocument>.Filter;

    private readonly MongoLoggingOptions _options = options.Value;

    private int _indexesEnsured;

    private IMongoCollection<ProcessBootDocument> Boots =>
        field ??= context.Database.GetCollection<ProcessBootDocument>(_options.ProcessBootsCollection);

    public async Task<bool?> RecordAsync(ProcessBoot boot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(boot);

        if (!context.IsActive)
        {
            return null;
        }

        await EnsureIndexesAsync(ct);

        string? previousVersion = null;
        var versionChanged = false;
        if (boot.Version is not null)
        {
            // The previous boot that knew its version: an unversioned restart in between (a
            // manual redeploy) neither is a deploy nor hides one.
            previousVersion = await Boots
                .Find(Filter.And(
                    Filter.Eq(doc => doc.ProcessName, boot.ProcessName),
                    Filter.Eq(doc => doc.Role, boot.Role),
                    Filter.Ne(doc => doc.Version, null)))
                .SortByDescending(doc => doc.StartedAtUtc)
                .Limit(1)
                .Project(doc => doc.Version)
                .FirstOrDefaultAsync(ct);
            versionChanged = ProcessVersions.IsVersionChange(previousVersion, boot.Version);
        }

        await Boots.InsertOneAsync(
            new ProcessBootDocument
            {
                ProcessName = boot.ProcessName,
                Role = boot.Role,
                Environment = boot.Environment,
                Version = boot.Version,
                PreviousVersion = previousVersion,
                VersionChanged = versionChanged,
                StartedAtUtc = boot.StartedAtUtc
            },
            cancellationToken: ct);

        return versionChanged;
    }

    public async Task<ProcessDeploymentWindow> GetDeploymentsAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct)
    {
        if (!context.IsActive)
        {
            return new ProcessDeploymentWindow([], []);
        }

        var inWindow = await Boots
            .Find(Filter.And(
                Filter.Eq(doc => doc.VersionChanged, true),
                Filter.Gte(doc => doc.StartedAtUtc, fromUtc),
                Filter.Lte(doc => doc.StartedAtUtc, toUtc)))
            .SortBy(doc => doc.StartedAtUtc)
            .ToListAsync(ct);

        // The last change before the window, per process and role. Deploys are kept for good
        // but are a handful a week, so the group runs over a few hundred rows at most.
        IReadOnlyList<BsonDocument> stages =
        [
            new BsonDocument("$match", new BsonDocument
            {
                { "versionChanged", true },
                { "startedAtUtc", new BsonDocument("$lt", fromUtc) }
            }),
            new BsonDocument("$sort", new BsonDocument("startedAtUtc", -1)),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument { { "processName", "$processName" }, { "role", "$role" } } },
                { "doc", new BsonDocument("$first", "$$ROOT") }
            }),
            new BsonDocument("$replaceRoot", new BsonDocument("newRoot", "$doc"))
        ];
        var pipeline = PipelineDefinition<ProcessBootDocument, ProcessBootDocument>.Create(stages);
        using var cursor = await Boots.AggregateAsync(pipeline, cancellationToken: ct);
        var before = await cursor.ToListAsync(ct);

        return new ProcessDeploymentWindow(
            before.Select(ToDeployment).ToList(),
            inWindow.Select(ToDeployment).ToList());
    }

    private static ProcessDeployment ToDeployment(ProcessBootDocument document) =>
        new(
            document.ProcessName,
            document.Role,
            document.Version ?? string.Empty,
            document.PreviousVersion,
            DateTime.SpecifyKind(document.StartedAtUtc, DateTimeKind.Utc));

    private async Task EnsureIndexesAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _indexesEnsured) != 0)
        {
            return;
        }

        var keys = Builders<ProcessBootDocument>.IndexKeys;
        var models = new List<CreateIndexModel<ProcessBootDocument>>
        {
            // The previous-boot probe each new boot makes.
            new(keys.Ascending(doc => doc.ProcessName).Ascending(doc => doc.Role).Descending(doc => doc.StartedAtUtc),
                new CreateIndexOptions { Name = "ix_process_role_started" }),
            // The deploy read: changes only, by time.
            new(keys.Ascending(doc => doc.VersionChanged).Ascending(doc => doc.StartedAtUtc),
                new CreateIndexOptions { Name = "ix_changed_started" })
        };
        await Boots.Indexes.CreateManyAsync(models, ct);
        await EnsureRestartsTtlAsync(ct);

        Volatile.Write(ref _indexesEnsured, 1);
    }

    /// <summary>
    /// A PARTIAL TTL index: only restarts expire, a deploy is kept for good. A changed retention
    /// rebuilds the index rather than failing every boot on an options conflict.
    /// </summary>
    private async Task EnsureRestartsTtlAsync(CancellationToken ct)
    {
        if (_options.ProcessBootRestartsRetention <= TimeSpan.Zero)
        {
            return;
        }

        var model = new CreateIndexModel<ProcessBootDocument>(
            Builders<ProcessBootDocument>.IndexKeys.Ascending(doc => doc.StartedAtUtc),
            new CreateIndexOptions<ProcessBootDocument>
            {
                Name = RestartsTtlIndexName,
                ExpireAfter = _options.ProcessBootRestartsRetention,
                PartialFilterExpression = Filter.Eq(doc => doc.VersionChanged, false)
            });

        try
        {
            await Boots.Indexes.CreateOneAsync(model, cancellationToken: ct);
        }
        catch (MongoCommandException ex) when (IndexConflictCodes.Contains(ex.Code))
        {
            await Boots.Indexes.DropOneAsync(RestartsTtlIndexName, ct);
            await Boots.Indexes.CreateOneAsync(model, cancellationToken: ct);
        }
    }
}
