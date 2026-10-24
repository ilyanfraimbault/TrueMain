namespace TrueMain.ReadModels.Ops;

/// <summary>
/// A page of known players for the admin (#2019), most recently updated first. <see cref="Total"/>
/// counts the rows matching the filters before paging.
/// </summary>
public sealed record PersonasPageReadModel
{
    public IReadOnlyList<PersonaListRowReadModel> Personas { get; init; } = [];

    public long Total { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }
}

/// <summary>One known player in the admin list: who, from where, and what still needs the operator.</summary>
public sealed record PersonaListRowReadModel
{
    public Guid Id { get; init; }

    public string Slug { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    /// <summary>The source that first published the persona.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>True when an operator set the name, which the sources then no longer overwrite.</summary>
    public bool DisplayNameLocked { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; init; }

    /// <summary>When an operator removed the persona; null for a live one.</summary>
    public DateTime? RemovedAtUtc { get; init; }

    /// <summary>Accounts linked to a tracked account: the ones the public page shows.</summary>
    public int TrackedAccounts { get; init; }

    /// <summary>Accounts a source lists that no tracked account answers to yet.</summary>
    public int UnresolvedAccounts { get; init; }

    public int Links { get; init; }

    /// <summary>Open conflicts the persona is part of, as the claimant or as the holder.</summary>
    public int OpenConflicts { get; init; }
}

/// <summary>
/// One known player in full for the admin's edit panel (#2019): every account and link with its row
/// id and source, what the persona may no longer claim, and its open conflicts.
/// </summary>
public sealed record PersonaAdminDetailReadModel
{
    public Guid Id { get; init; }

    public string Slug { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public bool DisplayNameLocked { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; init; }

    public DateTime? RemovedAtUtc { get; init; }

    /// <summary>The slug of the persona this one was merged into; null unless merged.</summary>
    public string? MergedIntoSlug { get; init; }

    public IReadOnlyList<PersonaAdminAccountReadModel> Accounts { get; init; } = [];

    public IReadOnlyList<PersonaAdminLinkReadModel> Links { get; init; } = [];

    /// <summary>Riot accounts the persona may no longer claim, newest first.</summary>
    public IReadOnlyList<PersonaAdminExclusionReadModel> Exclusions { get; init; } = [];

    /// <summary>URLs an operator removed, which the sources can no longer add back.</summary>
    public IReadOnlyList<PersonaAdminRemovedLinkReadModel> RemovedLinks { get; init; } = [];

    /// <summary>Open conflicts over the persona's accounts or its claims, most recently seen first.</summary>
    public IReadOnlyList<PersonaAdminConflictReadModel> Conflicts { get; init; } = [];
}

public sealed record PersonaAdminAccountReadModel
{
    /// <summary>The <c>persona_accounts</c> row, which the unlink action addresses.</summary>
    public Guid Id { get; init; }

    /// <summary>The Riot ID as the source published it.</summary>
    public string GameName { get; init; } = string.Empty;

    public string TagLine { get; init; } = string.Empty;

    public string PlatformId { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    /// <summary>The tracked account it answers to; null while untracked.</summary>
    public Guid? RiotAccountId { get; init; }

    /// <summary>The tracked account's current Riot ID (it follows renames); null when untracked.</summary>
    public string? CurrentGameName { get; init; }

    public string? CurrentTagLine { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}

public sealed record PersonaAdminLinkReadModel
{
    public Guid Id { get; init; }

    /// <summary>Lowercase kind, as on the public page.</summary>
    public string Kind { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }
}

public sealed record PersonaAdminExclusionReadModel
{
    public string GameName { get; init; } = string.Empty;

    public string TagLine { get; init; } = string.Empty;

    public string PlatformId { get; init; } = string.Empty;

    /// <summary><c>conflict</c> (a resolution), <c>unlinked</c> (an operator's unlink) or <c>takedown</c>.</summary>
    public string Reason { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }
}

public sealed record PersonaAdminRemovedLinkReadModel
{
    public string Url { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }
}

/// <summary>An open conflict seen from one persona: which account, and the other persona.</summary>
public sealed record PersonaAdminConflictReadModel
{
    public Guid Id { get; init; }

    /// <summary><c>claimant</c> when this persona claims an account another holds, <c>owner</c> when it holds one another claims.</summary>
    public string Role { get; init; } = string.Empty;

    public string GameName { get; init; } = string.Empty;

    public string TagLine { get; init; } = string.Empty;

    public string PlatformId { get; init; } = string.Empty;

    public string OtherSlug { get; init; } = string.Empty;

    public string OtherDisplayName { get; init; } = string.Empty;

    /// <summary>The source of the claim.</summary>
    public string Source { get; init; } = string.Empty;

    public DateTime LastSeenAtUtc { get; init; }
}

/// <summary>What an admin edit of a known player did (#2019).</summary>
public sealed record PersonaEditedResponse
{
    public Guid Id { get; init; }

    public string Slug { get; init; } = string.Empty;

    /// <summary>For a create or an added account: each account's outcome, as the batch answers it.</summary>
    public IReadOnlyList<PersonaAccountUpsertResult> Accounts { get; init; } = [];
}
