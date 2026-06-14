using Microsoft.Extensions.Logging;

namespace Data.Logging;

/// <summary>
/// Catalog of the named domain events ("ops events") the pipeline emits for
/// operator-facing milestones — e.g. a main candidate finishing validation or a
/// manual seed request reaching its terminal state (#444). Writers (Ingestor
/// processes) log them through the standard <see cref="ILogger"/> API using these
/// <see cref="EventId"/>s; the Mongo sink (<c>MongoLogger</c>) recognises them via
/// <see cref="Resolve"/> and persists them from <see cref="PersistedFloor"/> up,
/// below its usual Warning floor, stamping the event name into the document's
/// <c>eventType</c> so <c>GET /ops/logs</c> can filter on it. The admin Logs panel
/// builds its event select from <see cref="KnownEventTypes"/> — a static list, so
/// no Mongo <c>distinct</c> runs on every page load.
/// </summary>
/// <remarks>
/// Event names are a public, stable contract: they are persisted in log documents
/// and used as filter values, so renaming one orphans existing rows. Treat the
/// catalog as append-only and never recycle an id. Ids live in a reserved 1xxx
/// range to keep them visually distinct from framework event ids.
/// </remarks>
public static class OpsEvents
{
    /// <summary>
    /// The minimum level at which a registered ops event is persisted by the
    /// Mongo sink. Anything below (Debug/Trace) is dropped, registered or not.
    /// </summary>
    public const LogLevel PersistedFloor = LogLevel.Information;

    /// <summary>A main candidate finished match ingestion and was promoted to Validated.</summary>
    public static readonly EventId CandidateValidated = new(1000, nameof(CandidateValidated));

    /// <summary>A manual seed request resolved to a Riot account and its candidates were queued.</summary>
    public static readonly EventId SeedRequestResolved = new(1001, nameof(SeedRequestResolved));

    /// <summary>A manual seed request terminally failed (Riot ID not found, or resolution threw).</summary>
    public static readonly EventId SeedRequestFailed = new(1002, nameof(SeedRequestFailed));

    /// <summary>A discovery pass over one platform's ladder completed, with its counters.</summary>
    public static readonly EventId DiscoveryCycleCompleted = new(1003, nameof(DiscoveryCycleCompleted));

    /// <summary>A participant harvest pass completed, with its candidate/account counters.</summary>
    public static readonly EventId HarvestCycleCompleted = new(1004, nameof(HarvestCycleCompleted));

    // Single source for the lookup + the UI-facing list, so a new event only has
    // to be added in two places (its field above and this array).
    private static readonly EventId[] All =
    [
        CandidateValidated,
        SeedRequestResolved,
        SeedRequestFailed,
        DiscoveryCycleCompleted,
        HarvestCycleCompleted
    ];

    private static readonly Dictionary<string, int> IdByName =
        All.ToDictionary(eventId => eventId.Name!, eventId => eventId.Id, StringComparer.Ordinal);

    /// <summary>
    /// Every known event name, in catalog order. Exposed on the <c>/ops/logs</c>
    /// read model so the admin UI can populate its event filter select.
    /// </summary>
    public static IReadOnlyList<string> KnownEventTypes { get; } =
        All.Select(eventId => eventId.Name!).ToList();

    /// <summary>
    /// Returns the registered event name when <paramref name="eventId"/> is one of
    /// the ops events above, otherwise null. Both the id and the name must match,
    /// so a third-party event that happens to reuse one of the names (or ids)
    /// cannot be misclassified as a domain event.
    /// </summary>
    public static string? Resolve(EventId eventId)
        => eventId.Name is { Length: > 0 } name
           && IdByName.TryGetValue(name, out var id)
           && id == eventId.Id
            ? name
            : null;
}
