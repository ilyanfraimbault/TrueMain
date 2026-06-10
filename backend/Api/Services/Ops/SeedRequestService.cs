using Core.Lol.Identifiers;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Ops;

/// <summary>
/// Records a <c>SeedRequest</c> for the "seed by Riot ID" intake (#409).
/// <para>
/// The API intentionally does only the row insert here — it never calls Riot.
/// Resolving the PUUID, upserting the account and building its main candidates is
/// the Ingestor's job (ManualSeedProcess), which keeps this endpoint thin and
/// fast and avoids putting rate-limited Riot calls on a request thread.
/// </para>
/// </summary>
public sealed class SeedRequestService(TrueMainDbContext db, ILogger<SeedRequestService> logger) : ISeedRequestService
{
    // The platforms the pipeline actively tracks (mirrors Discovery's default
    // platform set). Requests outside this set are still accepted — Riot can
    // resolve them — but logged so an operator can spot a likely typo or an
    // account we don't otherwise crawl.
    private static readonly HashSet<string> TrackedPlatforms =
        new(StringComparer.Ordinal) { "EUW1", "KR", "NA1" };

    public async Task<SeedRequestCreateResult> CreateAsync(SeedRequestInput input, CancellationToken ct)
    {
        var gameName = input.GameName?.Trim();
        var tagLine = input.TagLine?.Trim()?.TrimStart('#');
        var platformRaw = input.PlatformId?.Trim();

        if (string.IsNullOrWhiteSpace(gameName))
        {
            return SeedRequestCreateResult.Invalid("gameName is required.");
        }

        if (string.IsNullOrWhiteSpace(tagLine))
        {
            return SeedRequestCreateResult.Invalid("tagLine is required.");
        }

        // Validate the platform against the real PlatformRoute set so a bad shard
        // never reaches the Ingestor (which would otherwise throw on ToRegional()).
        if (!PlatformId.TryParse(platformRaw, out var platformId))
        {
            return SeedRequestCreateResult.Invalid(
                $"platformId '{input.PlatformId}' is not a known platform route (e.g. EUW1, KR, NA1).");
        }

        var platform = platformId.Value;
        if (!TrackedPlatforms.Contains(platform))
        {
            logger.LogWarning(
                "Seed request for {GameName}#{TagLine} targets untracked platform {Platform}; accepting anyway.",
                gameName,
                tagLine,
                platform);
        }

        // Idempotency: if an unprocessed request (Pending or Resolving) for the
        // same Riot ID on the same platform already exists, return it instead of
        // queuing a duplicate. Matching is case-insensitive on name/tag since
        // Riot IDs are case-insensitive; the platform name is canonical. ILike
        // treats its pattern as a wildcard expression, so the name/tag MUST be
        // escaped — otherwise a request for gameName="%" would match (and return)
        // an arbitrary unrelated pending request on that platform.
        var gameNamePattern = LikeEscaping.Escape(gameName);
        var tagLinePattern = LikeEscaping.Escape(tagLine);
        var existing = await db.SeedRequests
            .Where(request =>
                (request.Status == SeedRequestStatus.Pending || request.Status == SeedRequestStatus.Resolving)
                && request.PlatformId == platform
                && EF.Functions.ILike(request.GameName, gameNamePattern, LikeEscaping.EscapeChar)
                && EF.Functions.ILike(request.TagLine, tagLinePattern, LikeEscaping.EscapeChar))
            .OrderBy(request => request.RequestedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            return new SeedRequestCreateResult
            {
                Id = existing.Id,
                Status = existing.Status.ToString(),
                Created = false
            };
        }

        var seedRequest = new SeedRequest
        {
            Id = Guid.NewGuid(),
            GameName = gameName,
            TagLine = tagLine,
            PlatformId = platform,
            Status = SeedRequestStatus.Pending,
            RequestedAtUtc = DateTime.UtcNow
        };

        db.SeedRequests.Add(seedRequest);
        await db.SaveChangesAsync(ct);

        return new SeedRequestCreateResult
        {
            Id = seedRequest.Id,
            Status = seedRequest.Status.ToString(),
            Created = true
        };
    }
}
