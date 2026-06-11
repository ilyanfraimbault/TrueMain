namespace TrueMain.ReadModels.Ops;

/// <summary>
/// A page of main-candidate rows for the admin Candidates panel. Rows are
/// newest/most-relevant first. <see cref="Total"/> is the count of all rows
/// matching the active filters (before paging) so the panel can render a pager.
/// </summary>
public sealed record CandidatesReadModel
{
    public IReadOnlyList<CandidateRowReadModel> Candidates { get; init; } = [];

    public long Total { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }
}

/// <summary>
/// A single <c>MainCandidate</c> as surfaced to the list. <see cref="Status"/> is
/// the <c>MainCandidateStatus</c> name ("New"/"Scored"/"Queued"/"Processing"/
/// "Validated"/"Rejected"). The Riot ID fields (<see cref="GameName"/>,
/// <see cref="TagLine"/>) are populated from the joined <c>RiotAccount</c> when one
/// exists for the candidate's PUUID; null when the account hasn't been resolved
/// yet (a candidate is discovered from mastery before its account is upserted).
/// </summary>
public sealed record CandidateRowReadModel
{
    public Guid Id { get; init; }

    public string PlatformId { get; init; } = string.Empty;

    public string Puuid { get; init; } = string.Empty;

    /// <summary>Joined from <c>RiotAccount</c> on PUUID; null when not yet resolved.</summary>
    public string? GameName { get; init; }

    /// <summary>Joined from <c>RiotAccount</c> on PUUID; null when not yet resolved.</summary>
    public string? TagLine { get; init; }

    public int ChampionId { get; init; }

    public long ChampionPoints { get; init; }

    public int ChampionRankInMasteryTop { get; init; }

    public double Score { get; init; }

    public string Status { get; init; } = string.Empty;

    public DateTime DiscoveredAtUtc { get; init; }

    public DateTime? ScoredAtUtc { get; init; }

    public DateTime? ValidatedAtUtc { get; init; }

    public DateTime LastPlayTimeUtc { get; init; }
}

/// <summary>
/// Full detail for one main-candidate: the candidate's own pipeline fields plus
/// the joined account identity, the count of match-participant rows already
/// ingested for the account's PUUID, and the linked <c>SeedRequest</c> when the
/// candidate's account was brought in by a manual "add a main" request (matched on
/// <c>ResolvedPuuid</c> + <c>PlatformId</c>). All three of the linked pieces are
/// optional: an organically-discovered, not-yet-ingested candidate carries none.
/// </summary>
public sealed record CandidateDetailReadModel
{
    public Guid Id { get; init; }

    public string PlatformId { get; init; } = string.Empty;

    public string Puuid { get; init; } = string.Empty;

    public string? GameName { get; init; }

    public string? TagLine { get; init; }

    public int ChampionId { get; init; }

    public long ChampionPoints { get; init; }

    public int ChampionRankInMasteryTop { get; init; }

    public double Score { get; init; }

    public string Status { get; init; } = string.Empty;

    public DateTime DiscoveredAtUtc { get; init; }

    public DateTime? ScoredAtUtc { get; init; }

    public DateTime? ValidatedAtUtc { get; init; }

    public DateTime LastPlayTimeUtc { get; init; }

    /// <summary>
    /// Number of <c>MatchParticipant</c> rows recorded for this candidate's PUUID,
    /// i.e. how many of the account's games have already been ingested. Zero before
    /// the Ingestor has processed the account.
    /// </summary>
    public long IngestedMatchCount { get; init; }

    /// <summary>
    /// The manual seed request that brought this candidate's account into the
    /// pipeline, matched on <c>ResolvedPuuid</c> + <c>PlatformId</c>; null when the
    /// candidate was surfaced organically by ladder discovery (no manual request).
    /// </summary>
    public SeedRequestReadModel? SeedRequest { get; init; }
}
