namespace TrueMain.ReadModels.Ops;

/// <summary>
/// A page of persona account conflicts for the admin queue (#2253), most recently seen first.
/// <see cref="Total"/> counts the rows matching the filters before paging; <see cref="Open"/> and
/// <see cref="Resolved"/> count the whole table, for the tab badges.
/// </summary>
public sealed record PersonaConflictsReadModel
{
    public IReadOnlyList<PersonaConflictReadModel> Conflicts { get; init; } = [];

    public long Total { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    public long Open { get; init; }

    public long Resolved { get; init; }
}

/// <summary>
/// One persona claiming a Riot account another persona holds. <see cref="Status"/> is
/// <c>Open</c> or <c>Resolved</c>; <see cref="Resolution"/> is <c>Moved</c>, <c>Kept</c>,
/// <c>Detached</c> or <c>Merged</c> once resolved.
/// </summary>
public sealed record PersonaConflictReadModel
{
    public Guid Id { get; init; }

    public string Status { get; init; } = string.Empty;

    public string? Resolution { get; init; }

    /// <summary>The source of the claim.</summary>
    public string Source { get; init; } = string.Empty;

    public DateTime FirstSeenAtUtc { get; init; }

    public DateTime LastSeenAtUtc { get; init; }

    public int TimesSeen { get; init; }

    public DateTime? ResolvedAtUtc { get; init; }

    public PersonaConflictAccountReadModel Account { get; init; } = new();

    /// <summary>The persona whose source lists the account.</summary>
    public PersonaConflictPersonaReadModel Claimant { get; init; } = new();

    /// <summary>The persona holding the account.</summary>
    public PersonaConflictPersonaReadModel Owner { get; init; } = new();

    /// <summary>What the two personas' links say about the answer (#2277); the open queue is ordered by it.</summary>
    public PersonaConflictEvidenceReadModel Evidence { get; init; } = new();
}

/// <summary>
/// The links two personas of a conflict share (#2277). Link kinds are lowercase, as on the public page.
/// <see cref="Score"/>: 10 per shared social channel, 3 per shared reference page, −5 per social
/// kind both list with different channels.
/// </summary>
public sealed record PersonaConflictEvidenceReadModel
{
    public int Score { get; init; }

    /// <summary>Social kinds (twitch, youtube, x…) whose channel both list.</summary>
    public IReadOnlyList<string> SharedSocial { get; init; } = [];

    /// <summary>Reference pages (Leaguepedia, LoLPros, DeepLoL, a website) both list.</summary>
    public int SharedReferences { get; init; }

    /// <summary>Social kinds both list, with different channels.</summary>
    public IReadOnlyList<string> DifferingSocial { get; init; } = [];
}

public sealed record PersonaConflictAccountReadModel
{
    /// <summary>The Riot ID as the claiming source published it.</summary>
    public string GameName { get; init; } = string.Empty;

    public string TagLine { get; init; } = string.Empty;

    public string PlatformId { get; init; } = string.Empty;

    /// <summary>The tracked account it answers to; null while TrueMain does not track it.</summary>
    public Guid? RiotAccountId { get; init; }

    /// <summary>The tracked account's current Riot ID (it follows renames); null when untracked.</summary>
    public string? CurrentGameName { get; init; }

    public string? CurrentTagLine { get; init; }
}

/// <summary>One side of a conflict, enough to tell the two apart in the list.</summary>
public sealed record PersonaConflictPersonaReadModel
{
    public Guid Id { get; init; }

    public string Slug { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public int AccountCount { get; init; }

    public int LinkCount { get; init; }
}

/// <summary>
/// One conflict with both personas in full — every account and link with its source — which is
/// what the operator reads to decide.
/// </summary>
public sealed record PersonaConflictDetailReadModel
{
    public PersonaConflictReadModel Conflict { get; init; } = new();

    public PersonaOpsReadModel Claimant { get; init; } = new();

    public PersonaOpsReadModel Owner { get; init; } = new();
}

/// <summary>A persona as the admin shows it: every account (tracked or not) and link, each with its source.</summary>
public sealed record PersonaOpsReadModel
{
    public Guid Id { get; init; }

    public string Slug { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }

    public IReadOnlyList<PersonaOpsAccountReadModel> Accounts { get; init; } = [];

    public IReadOnlyList<PersonaOpsLinkReadModel> Links { get; init; } = [];
}

public sealed record PersonaOpsAccountReadModel
{
    public string GameName { get; init; } = string.Empty;

    public string TagLine { get; init; } = string.Empty;

    public string PlatformId { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public Guid? RiotAccountId { get; init; }

    /// <summary>True for the account the conflict is about.</summary>
    public bool Disputed { get; init; }
}

public sealed record PersonaOpsLinkReadModel
{
    /// <summary>Lowercase kind, as on the public page.</summary>
    public string Kind { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;
}

/// <summary>What <c>POST /ops/personas/conflicts/{id}/resolve</c> did.</summary>
public sealed record PersonaConflictResolvedResponse
{
    public Guid Id { get; init; }

    public string Resolution { get; init; } = string.Empty;

    /// <summary>The persona that holds the account now; null when it was detached.</summary>
    public string? HolderSlug { get; init; }

    /// <summary>For a merge: the slug that now redirects to <see cref="HolderSlug"/>.</summary>
    public string? MergedSlug { get; init; }

    /// <summary>Other open conflicts the action settled too (a merge settles every conflict between the two).</summary>
    public int AlsoResolved { get; init; }
}

/// <summary>
/// A page of channel conflicts (#2286): live known players listing the same social channel, the
/// most channels in common first. Derived from the links, so there is no history: settled ones leave.
/// </summary>
public sealed record PersonaChannelConflictsReadModel
{
    public IReadOnlyList<PersonaChannelConflictReadModel> Conflicts { get; init; } = [];

    /// <summary>Conflicts matching the search, before paging.</summary>
    public long Total { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }
}

/// <summary>Two known players listing the same social channel(s), both in full.</summary>
public sealed record PersonaChannelConflictReadModel
{
    /// <summary>The one created first: the one a merge keeps by default.</summary>
    public PersonaOpsReadModel Older { get; init; } = new();

    public PersonaOpsReadModel Newer { get; init; } = new();

    /// <summary>The channels both list. Kinds are lowercase, as on the public page.</summary>
    public IReadOnlyList<PersonaSharedChannelReadModel> SharedChannels { get; init; } = [];
}

public sealed record PersonaSharedChannelReadModel
{
    public string Kind { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;
}
