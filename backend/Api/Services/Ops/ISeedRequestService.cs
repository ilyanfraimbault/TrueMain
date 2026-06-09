namespace TrueMain.Services.Ops;

/// <summary>
/// Write path for the "seed by Riot ID" intake (#409). Validates the request and
/// records (or de-duplicates) a <c>SeedRequest</c> row at <c>Pending</c>. The API
/// deliberately does no Riot work here — only the row insert — so the request
/// stays cheap and the heavy account-v1 + mastery resolution happens later in the
/// Ingestor's ManualSeedProcess. The shared backbone for the admin "add a main"
/// panel (#410) and bulk OTP import (#411).
/// </summary>
public interface ISeedRequestService
{
    Task<SeedRequestCreateResult> CreateAsync(SeedRequestInput input, CancellationToken ct);
}

/// <summary>Raw request body for <c>POST /ops/accounts/seed</c>.</summary>
public sealed record SeedRequestInput(string? GameName, string? TagLine, string? PlatformId);

/// <summary>
/// Outcome of recording a seed request. <see cref="Id"/>/<see cref="Status"/>
/// form the 202 body. <see cref="ValidationError"/> is non-null only when the
/// input was rejected (the controller maps it to a 400); <see cref="Created"/>
/// is false when an existing unprocessed request was returned instead of a new
/// row (idempotency), which the controller surfaces verbatim — still a 202.
/// </summary>
public sealed record SeedRequestCreateResult
{
    public Guid Id { get; init; }

    public string Status { get; init; } = string.Empty;

    public bool Created { get; init; }

    public string? ValidationError { get; init; }

    public bool IsValid => ValidationError is null;

    public static SeedRequestCreateResult Invalid(string error) => new() { ValidationError = error };
}
