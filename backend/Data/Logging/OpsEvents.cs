using Microsoft.Extensions.Logging;

namespace Data.Logging;

/// <summary>
/// Catalog of the named domain events ("ops events") the pipeline emits for
/// operator-facing milestones — e.g. a main candidate finishing validation or a
/// manual seed request reaching its terminal state (#444) — and for the request
/// outcomes an operator has to be able to count (#1555). Writers (Ingestor
/// processes, the API's request pipeline, the log sink itself) log them through
/// the standard <see cref="ILogger"/> API using these <see cref="EventId"/>s; the
/// Mongo sink (<c>MongoLogger</c>) recognises them via
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

    /// <summary>
    /// Reverting a claim back to Queued after an ingestion failure itself failed, so
    /// the account's candidates remain Processing until the claim lease expires (#263).
    /// </summary>
    public static readonly EventId MatchRevertFailed = new(1005, nameof(MatchRevertFailed));

    /// <summary>
    /// A recorded ingestor process run finished successfully (#722): every pipeline
    /// step (discovery, ingestion, aggregations, retention…) emits one per pass,
    /// with its duration and summary counters.
    /// </summary>
    public static readonly EventId ProcessRunCompleted = new(1006, nameof(ProcessRunCompleted));

    /// <summary>A recorded ingestor process run failed (#722); carries the exception.</summary>
    public static readonly EventId ProcessRunFailed = new(1007, nameof(ProcessRunFailed));

    /// <summary>
    /// A participant harvest pass could not cover its eligible pool: more (puuid,
    /// champion) pairs qualified than <c>Harvest:MaxCandidatesPerRun</c> allowed, so
    /// some were left for a later run (#495). Carries the dropped counts, per class
    /// (new discovery vs stat refresh) and per platform, so the operator can tell a
    /// harmless refresh backlog from new discovery actually starving.
    /// </summary>
    public static readonly EventId HarvestBudgetExhausted = new(1008, nameof(HarvestBudgetExhausted));

    /// <summary>
    /// A champion-mastery activity pass completed (#900): how many mains were checked,
    /// retired for inactivity, and brought back because their player returned.
    /// </summary>
    public static readonly EventId MainActivityCycleCompleted = new(1009, nameof(MainActivityCycleCompleted));

    /// <summary>
    /// The API rate limiter rejected a visitor (#1555). The first rejection of a
    /// partition in a window is logged with its request; the rest of that window is
    /// counted and published as one row per partition when the window closes, so a
    /// burst of thousands of 429s costs a handful of rows instead of flooding the
    /// bounded log channel.
    /// </summary>
    public static readonly EventId RateLimitRejected = new(1010, nameof(RateLimitRejected));

    /// <summary>An API request answered with a 5xx status (#1555); carries method, path, status, duration and traceId.</summary>
    public static readonly EventId RequestFailed = new(1011, nameof(RequestFailed));

    /// <summary>
    /// The client went away before the API answered (#1555) — a timeout upstream, a
    /// closed tab, a load generator giving up. Logged because ASP.NET only reports it
    /// at Debug, and under load it is often the first symptom.
    /// </summary>
    public static readonly EventId RequestAborted = new(1012, nameof(RequestAborted));

    /// <summary>
    /// The bounded log channel was full and evicted records before the sink could
    /// persist them (#1555). Written by the sink itself, straight into the batch, so
    /// a lossy burst is visible as a count instead of silently missing rows.
    /// </summary>
    public static readonly EventId LogRecordsDropped = new(1013, nameof(LogRecordsDropped));

    /// <summary>
    /// A frontend server — the public site or the admin portal — failed a request with a
    /// 5xx of its own: a render error, a handler that threw, the API proxy unable to reach
    /// the API (#1556). Reported through <c>POST /internal/logs</c>, one row per distinct
    /// error per flush, with the occurrence count.
    /// </summary>
    public static readonly EventId FrontendServerError = new(1014, nameof(FrontendServerError));

    /// <summary>
    /// A frontend's proxy relayed a 429 or a 5xx answer from the API (#1556): what the
    /// visitor saw, counted per route template and status.
    /// </summary>
    public static readonly EventId FrontendUpstreamErrors = new(1015, nameof(FrontendUpstreamErrors));

    // Single source for the lookup + the UI-facing list, so a new event only has
    // to be added in two places (its field above and this array).
    private static readonly EventId[] All =
    [
        CandidateValidated,
        SeedRequestResolved,
        SeedRequestFailed,
        DiscoveryCycleCompleted,
        HarvestCycleCompleted,
        MatchRevertFailed,
        ProcessRunCompleted,
        ProcessRunFailed,
        HarvestBudgetExhausted,
        MainActivityCycleCompleted,
        RateLimitRejected,
        RequestFailed,
        RequestAborted,
        LogRecordsDropped,
        FrontendServerError,
        FrontendUpstreamErrors
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
    /// The events a frontend may report through <c>POST /internal/logs</c> (#1556).
    /// Anything else is refused, so a forwarded row can never pass for a backend event
    /// such as <see cref="ProcessRunFailed"/>.
    /// </summary>
    public static IReadOnlyList<string> ForwardableEventTypes { get; } =
        [FrontendServerError.Name!, FrontendUpstreamErrors.Name!];

    /// <summary>The id of a registered event name, or null.</summary>
    public static int? IdOf(string name)
        => IdByName.TryGetValue(name, out var id) ? id : null;

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
