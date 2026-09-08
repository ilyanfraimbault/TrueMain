namespace TrueMain.Requests.Ops;

/// <summary>Request body for <c>POST /ops/accounts/freshness</c>.</summary>
public sealed record AccountFreshnessRequest
{
    /// <summary>
    /// Upper bound on one request. Keeps the scan bounded and the payload sane; a caller with
    /// more Riot IDs sends several batches rather than one unbounded query.
    /// </summary>
    public const int BatchLimit = 1000;

    public IReadOnlyList<AccountFreshnessRequestEntry>? Accounts { get; init; }
}

/// <summary>One Riot ID in an <see cref="AccountFreshnessRequest"/>.</summary>
public sealed record AccountFreshnessRequestEntry
{
    public string? GameName { get; init; }

    public string? TagLine { get; init; }

    public string? PlatformId { get; init; }
}
