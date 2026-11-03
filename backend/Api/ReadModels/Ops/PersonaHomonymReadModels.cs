namespace TrueMain.ReadModels.Ops;

/// <summary>What <c>POST /ops/personas/conflicts/auto-merge</c> did (#2271).</summary>
public sealed record PersonaAutoMergeResponse
{
    /// <summary>Same-name conflicts settled as merges.</summary>
    public int Merged { get; init; }

    /// <summary>Same-name personas sharing a social channel merged into their oldest (#2286).</summary>
    public int MergedByChannel { get; init; }

    /// <summary>Other open account conflicts those merges settled too.</summary>
    public int AlsoResolved { get; init; }

    /// <summary>
    /// Same-name conflicts that changed under the sweep, and same-name channel groups holding a pair
    /// marked as different people: they stay in the queue.
    /// </summary>
    public int Skipped { get; init; }
}

/// <summary>
/// A page of homonym groups (#2271): live known players sharing a name, grouped, in name order. A
/// group whose every pair an operator marked as different people is left out.
/// </summary>
public sealed record PersonaHomonymsReadModel
{
    public IReadOnlyList<PersonaHomonymGroupReadModel> Groups { get; init; } = [];

    /// <summary>Groups matching the search, before paging.</summary>
    public long Total { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }
}

public sealed record PersonaHomonymGroupReadModel
{
    /// <summary>The shared name key (lowercase, without spaces, dots, dashes or underscores).</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Oldest first: the one a merge keeps by default.</summary>
    public IReadOnlyList<PersonaHomonymReadModel> Personas { get; init; } = [];

    /// <summary>
    /// Links two or more of the group list (#2273) — the same channel, the strongest hint of one person —
    /// as their link key: lowercased, without the scheme, a <c>www.</c> or a trailing slash.
    /// </summary>
    public IReadOnlyList<string> SharedLinks { get; init; } = [];

    /// <summary>Pairs of the group already marked as different people, each as its two persona ids.</summary>
    public IReadOnlyList<IReadOnlyList<Guid>> DistinctPairs { get; init; } = [];
}

/// <summary>One persona of a homonym group, with what tells it apart from the others.</summary>
public sealed record PersonaHomonymReadModel
{
    public Guid Id { get; init; }

    public string Slug { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }

    public IReadOnlyList<PersonaOpsAccountReadModel> Accounts { get; init; } = [];

    public IReadOnlyList<PersonaOpsLinkReadModel> Links { get; init; } = [];
}

/// <summary>What <c>POST /ops/personas/homonyms/merge</c> did.</summary>
public sealed record PersonaHomonymMergeResponse
{
    /// <summary>The persona the others were merged into.</summary>
    public string KeptSlug { get; init; } = string.Empty;

    /// <summary>The slugs that now redirect to <see cref="KeptSlug"/>.</summary>
    public IReadOnlyList<string> MergedSlugs { get; init; } = [];

    /// <summary>Open conflicts between them the merge settled.</summary>
    public int AlsoResolved { get; init; }
}

/// <summary>What <c>POST /ops/personas/homonyms/distinct</c> did.</summary>
public sealed record PersonaHomonymDistinctResponse
{
    /// <summary>Pairs newly marked as different people (a pair marked before is not counted).</summary>
    public int Marked { get; init; }
}
