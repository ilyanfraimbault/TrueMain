using Data;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Truemains;

/// <summary>
/// The one place a public Riot ID becomes an account row. Every route that
/// takes a player identifier — the truemain profile, its match feed and match
/// detail, the player-scoped builds / matchups / performance panels, the rank
/// history, the activity grid, and the champion mains
/// comparison — resolves through here, so they can never disagree about which
/// account a given name tag means (#1230).
///
/// <para><b>Matching is case-insensitive.</b> A Riot ID reaches us as text a
/// human typed or pasted (<c>Name#TAG</c> in a search box, <c>Name-TAG</c> in a
/// URL someone re-typed by hand), not as an identity we issued — the PUUID is
/// that. Postgres' default collation is case-sensitive, so the nine copies of
/// this lookup that used <c>==</c> answered 404 for a Riot ID that differed
/// only in casing from the stored row, while the mains comparison (which
/// already lowered both halves) resolved the same input fine. Insensitive is
/// the semantics that matches what a user pastes, so it is the one kept.</para>
///
/// <para><b>Tiebreak: most recently active row wins.</b> A (gameName, tagLine)
/// pair is unique within a Riot routing region but can collide across regions,
/// and Riot IDs are recyclable, so a stale row and a renamed one legitimately
/// share one — which is why <c>IX_riot_accounts_GameName_TagLine_PlatformId</c>
/// is deliberately not unique (see <c>RiotAccountConfiguration</c>). Picking the
/// most recently active row is what a human looking for "this player" expects.
/// <c>Id</c> breaks an exact timestamp tie so two calls in the same request
/// cannot land on different rows.</para>
/// </summary>
public sealed class TruemainAccountResolver(TrueMainDbContext db)
{
    /// <summary>
    /// Resolves a Riot ID in either public form — the typed <c>Name#TAG</c> or
    /// the <c>{gameName}-{tagLine}</c> URL slug — to the account it designates,
    /// or <c>null</c> when the input is malformed or we hold no such account
    /// (callers turn that into a 404).
    /// </summary>
    public async Task<TruemainAccountRef?> ResolveAsync(string? nameTag, CancellationToken ct)
    {
        if (!NameTagParser.TryParseRiotId(nameTag, out var parsed))
        {
            return null;
        }

        // Lowered equality on both halves rather than ILIKE: equality sidesteps
        // LIKE metacharacters in raw user input entirely (no escaping to get
        // wrong), and `lower(col) = @p` is the exact expression a functional
        // index on (lower("GameName"), lower("TagLine")) would serve, which an
        // ILIKE could not use. Today no such index exists — the only index on
        // these columns is the plain case-sensitive one — so this is a scan of
        // riot_accounts; see #1230 for the measurement before adding one.
        //
        // Postgres' lower() follows the database collation and .NET's follows
        // the invariant culture; they agree on ASCII, which is what Riot tag
        // lines are, and the residual divergence on exotic game names is the
        // same one the mains comparison has always carried.
        var gameNameLower = parsed.GameName.ToLowerInvariant();
        var tagLineLower = parsed.TagLine.ToLowerInvariant();

        return await db.RiotAccounts
            .AsNoTracking()
            .Where(a => a.GameName.ToLower() == gameNameLower
                        && a.TagLine != null
                        && a.TagLine.ToLower() == tagLineLower)
            .OrderByDescending(a => a.LastMatchIngestAtUtc ?? a.UpdatedAtUtc)
            .ThenBy(a => a.Id)
            .Select(a => new TruemainAccountRef(
                a.Id,
                a.Puuid,
                a.GameName,
                a.TagLine,
                a.PlatformId,
                a.ProfileIconId,
                a.SummonerLevel))
            .FirstOrDefaultAsync(ct);
    }
}

/// <summary>
/// The account a public Riot ID resolved to, with the identity columns the
/// truemain and champion-comparison surfaces render. Stored casing comes from
/// the row, not from the request, so a profile always shows the Riot ID as Riot
/// spells it even when the URL was typed in another case.
/// </summary>
public sealed record TruemainAccountRef(
    Guid Id,
    string Puuid,
    string GameName,
    string? TagLine,
    string PlatformId,
    int ProfileIconId,
    int SummonerLevel);
