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

    public IReadOnlyList<PersonaAccountUpsertResult> Accounts { get; init; } = Array.Empty<PersonaAccountUpsertResult>();
}

public sealed record PersonaAccountUpsertResult
{
    public string GameName { get; init; } = string.Empty;

    public string TagLine { get; init; } = string.Empty;

    public string PlatformId { get; init; } = string.Empty;

    /// <summary>
    /// <c>linked</c> — resolved to a tracked account; <c>pending</c> — stored, not tracked yet;
    /// <c>seeded</c> — stored and queued through the seed intake; <c>conflict</c> — the Riot ID or
    /// its account already belongs to another persona, left untouched; <c>invalid</c> — malformed.
    /// </summary>
    public string Status { get; init; } = string.Empty;
}
