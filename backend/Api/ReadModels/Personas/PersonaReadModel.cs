namespace TrueMain.ReadModels.Personas;

/// <summary>
/// The persona a tracked account belongs to, as the leaderboard row and the profile carry it
/// (#2016): enough to show the person's name and link to <c>/players/{slug}</c>.
/// </summary>
public sealed record PersonaTagReadModel
{
    public string Slug { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;
}

/// <summary>The known players worth a sitemap entry (<c>GET /players</c>, #2018).</summary>
public sealed record PersonaDirectoryResponse
{
    public IReadOnlyList<PersonaDirectoryEntryReadModel> Players { get; init; } = Array.Empty<PersonaDirectoryEntryReadModel>();
}

public sealed record PersonaDirectoryEntryReadModel
{
    public string Slug { get; init; } = string.Empty;

    /// <summary>The persona's last upsert — the sitemap's <c>lastmod</c>.</summary>
    public DateTime UpdatedAtUtc { get; init; }
}

/// <summary>A known player and every account sources list for them (<c>GET /players/{slug}</c>).</summary>
public sealed record PersonaReadModel
{
    public string Slug { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Public channels and pages, ordered by kind.</summary>
    public IReadOnlyList<PersonaLinkReadModel> Links { get; init; } = Array.Empty<PersonaLinkReadModel>();

    /// <summary>Tracked accounts first, best rank first; then the accounts TrueMain does not track yet.</summary>
    public IReadOnlyList<PersonaAccountReadModel> Accounts { get; init; } = Array.Empty<PersonaAccountReadModel>();
}

public sealed record PersonaLinkReadModel
{
    /// <summary>Lowercase kind: <c>twitch</c>, <c>youtube</c>, <c>x</c>, <c>kick</c>, <c>tiktok</c>, <c>instagram</c>, <c>soop</c>, <c>chzzk</c>, <c>leaguepedia</c>, <c>lolpros</c>, <c>deeplol</c>, <c>website</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;
}

public sealed record PersonaAccountReadModel
{
    /// <summary>The account's current Riot ID when tracked (it follows renames), else the one the source published.</summary>
    public string GameName { get; init; } = string.Empty;

    public string TagLine { get; init; } = string.Empty;

    public string PlatformId { get; init; } = string.Empty;

    /// <summary><c>europe</c>, <c>americas</c> or <c>korea</c>; null for a platform the site does not expose.</summary>
    public string? Region { get; init; }

    /// <summary>True when TrueMain tracks the account, i.e. it has a profile page to link to.</summary>
    public bool Tracked { get; init; }

    /// <summary>Null when untracked.</summary>
    public int? ProfileIconId { get; init; }

    /// <summary>Latest ranked solo/duo standing; null when untracked or unranked.</summary>
    public PersonaAccountRankedReadModel? Ranked { get; init; }

    /// <summary>Up to three active mains, most played first; empty when untracked or never analysed.</summary>
    public IReadOnlyList<PersonaAccountChampionReadModel> TopChampions { get; init; } = Array.Empty<PersonaAccountChampionReadModel>();
}

public sealed record PersonaAccountRankedReadModel
{
    public string Tier { get; init; } = string.Empty;

    public string Division { get; init; } = string.Empty;

    public int LeaguePoints { get; init; }

    public int? Wins { get; init; }

    public int? Losses { get; init; }
}

public sealed record PersonaAccountChampionReadModel
{
    public int ChampionId { get; init; }

    public int Games { get; init; }

    /// <summary>Share of the account's games on this champion, 0..1.</summary>
    public double PlayRate { get; init; }

    public bool IsOtp { get; init; }
}
