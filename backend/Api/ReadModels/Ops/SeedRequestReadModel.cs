namespace TrueMain.ReadModels.Ops;

/// <summary>
/// A page of seed requests for the admin intake list, newest-first.
/// <see cref="Total"/> counts every row matching the active filters, before
/// paging, so the panel can render a pager and say how much of the queue is left.
/// <para>
/// Shaped like <c>CandidatesReadModel</c> on purpose: the two lists sit on the
/// same page and answer the same kind of question, so they page identically
/// (#1166).
/// </para>
/// </summary>
public sealed record SeedRequestsReadModel
{
    public IReadOnlyList<SeedRequestReadModel> Requests { get; init; } = [];

    public long Total { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }
}

/// <summary>
/// A single "seed by Riot ID" request as surfaced to the admin panel. The API
/// records the row at <c>Pending</c>; the Ingestor's ManualSeedProcess resolves
/// the Riot ID, upserts the account, and stamps the terminal state.
/// <see cref="Status"/> is the <c>SeedRequestStatus</c> name
/// ("Pending"/"Resolving"/"Ingested"/"Failed"). <see cref="Error"/> carries the
/// failure detail when <see cref="Status"/> is "Failed" (else null);
/// <see cref="ResolvedPuuid"/> and <see cref="ResolvedRiotAccountId"/> are
/// populated once ingested.
/// </summary>
public sealed record SeedRequestReadModel
{
    public Guid Id { get; init; }

    public string GameName { get; init; } = string.Empty;

    public string TagLine { get; init; } = string.Empty;

    public string PlatformId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string? Error { get; init; }

    public DateTime RequestedAtUtc { get; init; }

    public DateTime? ProcessedAtUtc { get; init; }

    public string? ResolvedPuuid { get; init; }

    public Guid? ResolvedRiotAccountId { get; init; }
}
