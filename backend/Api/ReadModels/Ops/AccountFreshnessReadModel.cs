namespace TrueMain.ReadModels.Ops;

/// <summary>
/// One Riot ID's intake state, for callers deciding whether an account is worth seeding.
///
/// <para>
/// Deliberately tiny next to <see cref="AccountExplorerReadModel"/>. That one traces a Riot ID
/// through the whole pipeline — identity, lease, candidate funnel, mains, rank history — which
/// is what an operator inspecting one player wants, and far too much to ask thousands of times
/// in a row. A bulk caller only needs three facts: do we have it, is it still usable, and when
/// did we last ingest it.
/// </para>
/// </summary>
public sealed record AccountFreshnessReadModel
{
    /// <summary>The Riot ID as the caller spelled it, so a response can be matched back.</summary>
    public string GameName { get; init; } = string.Empty;

    public string TagLine { get; init; } = string.Empty;

    public string PlatformId { get; init; } = string.Empty;

    /// <summary>False when no account carries this Riot ID on this platform.</summary>
    public bool Known { get; init; }

    /// <summary>
    /// <c>Active</c> or <c>Invalid</c> — the latter meaning account-v1 already 404'd on the
    /// stored puuid and we recorded it. Null when <see cref="Known"/> is false.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// When match ingestion last ran for this account. Null both when the account is unknown
    /// and when it is tracked but its claim has never come up — the caller separates those
    /// with <see cref="Known"/>. That second case is the one worth acting on: the account is
    /// in the population and has still never been fetched.
    /// </summary>
    public DateTime? LastMatchIngestAtUtc { get; init; }
}

/// <summary>Response for <c>POST /ops/accounts/freshness</c>.</summary>
public sealed record AccountFreshnessResponse
{
    public IReadOnlyList<AccountFreshnessReadModel> Accounts { get; init; } = [];
}
