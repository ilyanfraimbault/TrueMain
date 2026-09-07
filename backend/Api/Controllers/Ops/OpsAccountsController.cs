using Core.Lol.Identifiers;
using Microsoft.AspNetCore.Mvc;
using TrueMain.Http;
using TrueMain.ReadModels.Ops;
using TrueMain.Requests.Ops;
using TrueMain.Services.Ops.Accounts;
using TrueMain.Services.Truemains.Identity;

namespace TrueMain.Controllers.Ops;

/// <summary>
/// The accounts surface: the freshness batch a seeder checks before it sends anything, the seed
/// request itself and its status, and the per-account explorer. This is the only ops controller
/// that writes — seeding creates a row — which is reason enough to keep it apart from the
/// read-only panels.
/// </summary>
public sealed class OpsAccountsController(
    ISeedRequestService seedRequestService,
    ISeedRequestQueryService seedRequestQueryService,
    IAccountExplorerQueryService accountExplorerQueryService,
    IAccountFreshnessQueryService accountFreshnessQueryService) : OpsControllerBase
{
    /// <summary>
    /// Bulk counterpart to <c>GET accounts/{nameTag}</c>: for each Riot ID, whether we already
    /// track it, whether it is still usable, and when we last ingested it. Nothing else.
    /// <para>
    /// A POST because the input is a list, not because it writes — this is a read. It exists
    /// so a batch caller stops looping the account explorer: that endpoint traces one Riot ID
    /// through the entire pipeline, which is right for an operator and ruinous a few thousand
    /// times in a row (the OTP seeder's first run drove it into 30-second timeouts on the live
    /// site). Capped at <see cref="AccountFreshnessRequest.BatchLimit"/> entries per request so
    /// the query stays bounded; a caller with more sends several batches.
    /// </para>
    /// 400 for an entry missing a gameName/platformId, an unknown platform route, or a batch
    /// over the limit.
    /// </summary>
    [HttpPost("accounts/freshness")]
    [ProducesResponseType(typeof(AccountFreshnessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AccountFreshnessResponse>> GetAccountFreshnessAsync(
        [FromBody] AccountFreshnessRequest request,
        CancellationToken ct = default)
    {
        var entries = request.Accounts ?? [];
        if (entries.Count == 0)
        {
            return Ok(new AccountFreshnessResponse());
        }

        if (entries.Count > AccountFreshnessRequest.BatchLimit)
        {
            return ValidationProblem(
                $"accounts holds {entries.Count} entries; at most "
                + $"{AccountFreshnessRequest.BatchLimit} per request. Send several batches.");
        }

        var queries = new List<AccountFreshnessQuery>(entries.Count);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.GameName) || string.IsNullOrWhiteSpace(entry.PlatformId))
            {
                return ValidationProblem("every entry needs a gameName and a platformId.");
            }

            if (!PlatformId.TryParse(entry.PlatformId.Trim(), out var platform))
            {
                return ValidationProblem(PlatformParameter.InvalidMessage("platformId", entry.PlatformId));
            }

            queries.Add(new AccountFreshnessQuery(
                entry.GameName.Trim(), (entry.TagLine ?? string.Empty).Trim(), platform.Value));
        }

        var accounts = await accountFreshnessQueryService.GetAsync(queries, ct);
        return Ok(new AccountFreshnessResponse { Accounts = accounts });
    }

    /// <summary>
    /// Seeds a single account into the pipeline by its Riot ID (gameName +
    /// tagLine + platformId), instead of waiting for the ladder Discovery to
    /// surface it. Records a <c>SeedRequest</c> at <c>Pending</c> and returns 202;
    /// the Ingestor's ManualSeedProcess does the actual Riot resolution + account
    /// upsert later. Idempotent: an existing unprocessed (Pending/Resolving)
    /// request for the same Riot ID on the same platform is returned as-is rather
    /// than duplicated (still a 202). 400 for a missing name/tag or an unknown
    /// platform route.
    /// </summary>
    [HttpPost("accounts/seed")]
    [ProducesResponseType(typeof(SeedRequestAcceptedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SeedRequestAcceptedResponse>> SeedAccountAsync(
        [FromBody] SeedAccountRequest request,
        CancellationToken ct = default)
    {
        var result = await seedRequestService.CreateAsync(
            new SeedRequestInput(request.GameName, request.TagLine, request.PlatformId),
            ct);

        if (!result.IsValid)
        {
            return ValidationProblem(result.ValidationError!);
        }

        // 202 whether the row was freshly created or an existing unprocessed one
        // was returned (idempotency): in both cases the work is accepted and
        // pending, and the caller polls GET /ops/accounts/seed/{id} for progress.
        return Accepted(new SeedRequestAcceptedResponse
        {
            Id = result.Id,
            Status = result.Status,
            Created = result.Created
        });
    }

    [HttpGet("accounts/seed/{id:guid}")]
    [ProducesResponseType(typeof(SeedRequestReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SeedRequestReadModel>> GetSeedRequestAsync(Guid id, CancellationToken ct = default)
    {
        var readModel = await seedRequestQueryService.GetByIdAsync(id, ct);
        return readModel is null ? NotFound() : Ok(readModel);
    }

    /// <summary>
    /// Manual seed ("add a main") requests, newest-first, paged (#1166).
    /// <paramref name="status"/> is an exact <c>SeedRequestStatus</c> name
    /// (case-insensitive; unknown values are ignored) and <paramref name="search"/> is a
    /// case-insensitive substring match on the Riot ID (gameName/tagLine).
    /// </summary>
    /// <remarks>
    /// Paged rather than capped at a "recent" limit because the queue is no longer
    /// operator-sized: one weekly OTP seeder run adds tens of thousands of requests, and a
    /// list that can only show its newest page cannot answer how much is left to drain.
    /// The response carries the filtered total for exactly that.
    /// </remarks>
    /// <param name="status">
    /// Restrict to a single <c>SeedRequestStatus</c> (Pending/Resolving/Ingested/Failed).
    /// Omit for all.
    /// </param>
    /// <param name="search">Riot ID substring (gameName/tagLine). Omit for none.</param>
    /// <param name="region">Restrict to one PlatformId (e.g. "EUW1"). Omit for all.</param>
    /// <param name="page">1-based page index (backend clamps to ≥ 1).</param>
    /// <param name="pageSize">Rows per page (backend clamps to [1, 100], default 25).</param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("accounts/seed")]
    [ProducesResponseType(typeof(SeedRequestsReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SeedRequestsReadModel>> GetSeedRequestsAsync(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] string? region,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct = default)
    {
        // Unlike `status`, a bad region is rejected rather than ignored: the store matches
        // it exactly, so a typo would return an empty page, which reads as "no requests in
        // this region" instead of "that is not a region". Same check the seed and freshness
        // endpoints above already apply to a platform.
        if (!this.TryNormalizeOptionalPlatform(region, nameof(region), out var platformId, out var regionProblem))
        {
            return regionProblem;
        }

        var readModel = await seedRequestQueryService.GetPageAsync(
            status, search, platformId, page, pageSize, ct);
        return Ok(readModel);
    }

    /// <summary>
    /// Traces one Riot ID through the whole pipeline (#1032): identity and refresh
    /// state, the match-ingestion lease, the candidate funnel, the analysed main
    /// champions and the rank history, in one read-model. Read-only, and
    /// database-only — the API holds no Riot client, so this never resolves a Riot
    /// ID that the pipeline has not already recorded.
    /// <para>
    /// Declared after the literal <c>accounts/seed</c> routes; literal segments
    /// outrank the <c>{nameTag}</c> parameter, so those keep resolving to the seed
    /// endpoints.
    /// </para>
    /// </summary>
    /// <param name="nameTag">
    /// The Riot ID, either as typed (<c>Name#TAG</c>, percent-encoded) or in the
    /// hyphen slug form the public routes use (<c>Name-TAG</c>). 400 when it parses
    /// as neither.
    /// </param>
    /// <param name="region">
    /// Restrict the search to one platform (e.g. "EUW1"). Omit to search every
    /// region — a Riot ID is only unique within a routing region, so the read-model
    /// lists any other account carrying it. 400 on an unknown platform, because
    /// silently answering "never discovered" for a typo would be a lie.
    /// </param>
    /// <param name="ct">Request cancellation token.</param>
    [HttpGet("accounts/{nameTag}")]
    [ProducesResponseType(typeof(AccountExplorerReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AccountExplorerReadModel>> GetAccountExplorerAsync(
        string nameTag,
        [FromQuery] string? region,
        CancellationToken ct = default)
    {
        if (!NameTagParser.TryParseRiotId(nameTag, out var parsed))
        {
            return ValidationProblem(
                $"nameTag must be a Riot ID of the form Name#TAG or Name-TAG "
                + $"(at most {NameTagParser.MaxRiotIdLength} characters).");
        }

        if (!this.TryNormalizeOptionalPlatform(region, nameof(region), out var platformId, out var regionProblem))
        {
            return regionProblem;
        }

        // No 404: an unknown Riot ID is a state this endpoint exists to report,
        // and a 404 would render in the admin as a failure rather than an answer.
        var readModel = await accountExplorerQueryService.GetAsync(
            parsed.GameName, parsed.TagLine, platformId, ct);

        return Ok(readModel);
    }
}
