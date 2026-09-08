namespace TrueMain.ReadModels.Ops;

/// <summary>
/// 202 body for an accepted seed request: the row id, its current status, and whether it was
/// created by this call. <see cref="Created"/> is <c>true</c> for a freshly-inserted request
/// and <c>false</c> when an existing unprocessed request was returned instead (idempotency) —
/// letting the caller tell a brand-new seed apart from an "already seeded" hit.
/// </summary>
public sealed record SeedRequestAcceptedResponse
{
    public Guid Id { get; init; }

    public string Status { get; init; } = string.Empty;

    public bool Created { get; init; }
}
