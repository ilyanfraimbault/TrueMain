namespace TrueMain.Requests.Ops;

/// <summary>
/// Body of <c>POST /ops/personas</c> (#2019): a known player an operator adds by hand. Everything it
/// writes carries the source <c>manual</c>, and its name is the operator's from then on.
/// </summary>
public sealed record PersonaCreateRequest
{
    /// <summary>Lowercase kebab URL key, 1–64 characters of <c>[a-z0-9-]</c>, never used before.</summary>
    public string? Slug { get; init; }

    public string? DisplayName { get; init; }

    public IReadOnlyList<PersonaLinkEntry>? Links { get; init; }

    public IReadOnlyList<PersonaAccountEntry>? Accounts { get; init; }
}

/// <summary>Body of <c>PATCH /ops/personas/{id}</c> (#2019): the operator's name for the person.</summary>
public sealed record PersonaRenameRequest
{
    public string? DisplayName { get; init; }
}
