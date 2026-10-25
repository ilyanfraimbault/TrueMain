namespace TrueMain.ReadModels.Ops;

/// <summary>Response of <c>POST /ops/personas/batch</c>: one result per requested persona, in order.</summary>
public sealed record PersonaBatchResponse
{
    public IReadOnlyList<PersonaUpsertResult> Personas { get; init; } = Array.Empty<PersonaUpsertResult>();
}

public sealed record PersonaUpsertResult
{
    public string Slug { get; init; } = string.Empty;

    /// <summary>True when this call created the persona, false when it updated an existing one.</summary>
    public bool Created { get; init; }

    /// <summary>Why the persona was skipped as a whole (malformed slug, name or source); null when it was written.</summary>
    public string? Error { get; init; }

    /// <summary>
    /// The slug of the persona this entry fed instead, when an operator merged the requested one into
    /// it (#2253); null otherwise.
    /// </summary>
    public string? MergedInto { get; init; }

    /// <summary>
    /// True when an operator removed this persona (#2019): the entry was ignored and nothing was
    /// written. Not an error — the source still lists the person, the operator decided otherwise.
    /// </summary>
    public bool Removed { get; init; }

    public IReadOnlyList<PersonaAccountUpsertResult> Accounts { get; init; } = Array.Empty<PersonaAccountUpsertResult>();
}

public sealed record PersonaAccountUpsertResult
{
    public string GameName { get; init; } = string.Empty;

    public string TagLine { get; init; } = string.Empty;

    public string PlatformId { get; init; } = string.Empty;

    /// <summary>
    /// <c>linked</c> — resolved to a tracked account; <c>pending</c> — stored, not tracked yet;
    /// <c>seeded</c> — stored and queued through the seed intake; <c>untracked</c> — stored, on a
    /// platform the pipeline does not ingest, so never seeded; <c>conflict</c> — the Riot ID or
    /// its account already belongs to another persona, left untouched and recorded for the admin's
    /// conflict queue; <c>excluded</c> — an operator settled a conflict over it against this persona,
    /// unlinked it from this persona, or took it down with a removed persona, so the claim is ignored; <c>invalid</c> — malformed, including a placeholder such as
    /// <c>undefined</c> for a name or tag.
    /// </summary>
    public string Status { get; init; } = string.Empty;
}
