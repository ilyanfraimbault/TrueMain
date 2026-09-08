namespace TrueMain.Requests.Ops;

/// <summary>Request body for <c>POST /ops/accounts/seed</c>.</summary>
public sealed record SeedAccountRequest
{
    public string? GameName { get; init; }

    public string? TagLine { get; init; }

    public string? PlatformId { get; init; }
}
