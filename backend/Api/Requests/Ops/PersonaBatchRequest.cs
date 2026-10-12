namespace TrueMain.Requests.Ops;

/// <summary>Request body for <c>POST /ops/personas/batch</c> (#2016).</summary>
public sealed record PersonaBatchRequest
{
    /// <summary>
    /// Upper bound on one request: each persona costs a few indexed lookups and possibly a seed
    /// request per untracked account, so a scraper with more sends several batches.
    /// </summary>
    public const int BatchLimit = 100;

    public IReadOnlyList<PersonaUpsertEntry>? Personas { get; init; }
}

/// <summary>
/// One known player as a source publishes them. Merging the same person across sources is the
/// caller's job; the endpoint keys on <see cref="Slug"/>.
/// </summary>
public sealed record PersonaUpsertEntry
{
    /// <summary>Lowercase kebab URL key (<c>irelking</c>), 1–64 characters of <c>[a-z0-9-]</c>.</summary>
    public string? Slug { get; init; }

    public string? DisplayName { get; init; }

    /// <summary>The publishing source: <c>leaguepedia</c>, <c>lolpros</c>, <c>trackingthepros</c>, <c>dpm</c>, <c>deeplol</c> or <c>manual</c>.</summary>
    public string? Source { get; init; }

    public IReadOnlyList<PersonaLinkEntry>? Links { get; init; }

    public IReadOnlyList<PersonaAccountEntry>? Accounts { get; init; }

    /// <summary>Queue an untracked account through the seed intake, so a later upsert can link it.</summary>
    public bool SeedMissing { get; init; }
}

/// <summary>One public channel or page of a persona.</summary>
public sealed record PersonaLinkEntry
{
    /// <summary><c>twitch</c>, <c>youtube</c>, <c>x</c>, <c>kick</c>, <c>tiktok</c>, <c>instagram</c>, <c>leaguepedia</c>, <c>lolpros</c> or <c>website</c>.</summary>
    public string? Kind { get; init; }

    public string? Url { get; init; }
}

/// <summary>One Riot account a source lists for a persona.</summary>
public sealed record PersonaAccountEntry
{
    public string? GameName { get; init; }

    public string? TagLine { get; init; }

    public string? PlatformId { get; init; }
}
