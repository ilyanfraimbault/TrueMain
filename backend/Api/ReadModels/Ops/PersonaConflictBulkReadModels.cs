namespace TrueMain.ReadModels.Ops;

/// <summary>
/// What <c>POST /ops/personas/conflicts/bulk-resolve</c> would settle at <see cref="MinScore"/>
/// (#2295): open conflicts whose two known players share a social channel and list no differing
/// one, most certain first. <see cref="Eligible"/> counts them all; <see cref="Conflicts"/> holds at
/// most <see cref="Limit"/>, the batch the operator confirms.
/// </summary>
public sealed record PersonaConflictBulkPreviewReadModel
{
    public int MinScore { get; init; }

    public int Limit { get; init; }

    public int Eligible { get; init; }

    public IReadOnlyList<PersonaConflictBulkCandidateReadModel> Conflicts { get; init; } = [];
}

/// <summary>One conflict of the preview and the merge it would get: into the older known player.</summary>
public sealed record PersonaConflictBulkCandidateReadModel
{
    public PersonaConflictReadModel Conflict { get; init; } = new();

    /// <summary><c>claimant</c> or <c>owner</c>: the side that stays.</summary>
    public string Keep { get; init; } = string.Empty;

    public string KeptSlug { get; init; } = string.Empty;

    /// <summary>The slug that redirects to <see cref="KeptSlug"/> once merged.</summary>
    public string MergedSlug { get; init; } = string.Empty;
}

/// <summary>What a bulk resolution did, one outcome per requested conflict in request order.</summary>
public sealed record PersonaConflictBulkResolveResponse
{
    public int Resolved { get; init; }

    public int Skipped { get; init; }

    public IReadOnlyList<PersonaConflictBulkOutcomeReadModel> Outcomes { get; init; } = [];
}

/// <summary>
/// One conflict of a bulk resolution. <see cref="Status"/> is <c>resolved</c> or <c>skipped</c>;
/// a skip names its <see cref="Reason"/>: <c>notFound</c>, <c>notOpen</c> (resolved since, often by an
/// earlier merge of the same batch), <c>differingChannel</c>, <c>belowFloor</c> (under the floor, or no shared social channel), or <c>changed</c>
/// (one of the known players was merged or removed since).
/// </summary>
public sealed record PersonaConflictBulkOutcomeReadModel
{
    public Guid Id { get; init; }

    public string Status { get; init; } = string.Empty;

    public string? Reason { get; init; }

    /// <summary>The single resolution's answer, for a resolved one.</summary>
    public PersonaConflictResolvedResponse? Resolution { get; init; }
}
