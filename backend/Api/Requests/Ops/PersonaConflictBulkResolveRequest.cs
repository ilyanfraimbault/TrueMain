namespace TrueMain.Requests.Ops;

/// <summary>
/// Body of <c>POST /ops/personas/conflicts/bulk-resolve</c> (#2295): the conflicts the operator
/// previewed, never a query re-evaluated at click time. The server re-checks each one against
/// <see cref="MinScore"/> before merging it.
/// </summary>
public sealed record PersonaConflictBulkResolveRequest
{
    public IReadOnlyList<Guid>? Ids { get; init; }

    /// <summary>The floor the preview was drawn at; at least one shared social channel (10).</summary>
    public int? MinScore { get; init; }
}
