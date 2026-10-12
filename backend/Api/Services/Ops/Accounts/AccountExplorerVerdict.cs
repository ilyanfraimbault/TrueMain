using System.Globalization;
using Data.Entities;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Accounts;

/// <summary>
/// The account explorer's verdicts (#1032), computed from rows already read: which
/// pipeline state the account is in, the sentence that explains it, and whether
/// retention has demonstrably deleted its games. Pure — no database, no clock —
/// so every rung of the ladder is testable from hand-built rows.
/// </summary>
internal static class AccountExplorerVerdict
{
    /// <summary>
    /// The first matching rung of <see cref="AccountPipelineState"/> for an account
    /// that has a <c>riot_accounts</c> row. The two row-less states are decided by
    /// the caller, which is the only place that knows the row is missing.
    /// </summary>
    internal static AccountPipelineState ResolveState(
        RiotAccount account,
        IReadOnlyList<AccountExplorerMainRowReadModel> mainRows,
        IReadOnlyList<AccountExplorerCandidateReadModel> candidates)
    {
        // Invalid comes first: nothing downstream will ever move again, so any
        // other label would describe a state the account can no longer leave.
        if (account.Status == RiotAccountStatus.Invalid)
        {
            return AccountPipelineState.Invalidated;
        }

        if (HasActiveMain(mainRows) || IsInNewCandidateArm(account, candidates))
        {
            return AccountPipelineState.Tracked;
        }

        if (mainRows.Any(m => m.IsMain))
        {
            return AccountPipelineState.Retired;
        }

        if (mainRows.Count > 0)
        {
            return AccountPipelineState.NotAMain;
        }

        return candidates.Count > 0
            ? AccountPipelineState.CandidateOnly
            : AccountPipelineState.Discovered;
    }

    /// <summary>One of the two membership arms of the real ingest claim.</summary>
    internal static bool HasActiveMain(IReadOnlyList<AccountExplorerMainRowReadModel> mainRows)
        => mainRows.Any(m => m is { IsMain: true, IsActive: true });

    /// <summary>Whether any candidate sits at <c>Queued</c> — on its own, not a claim arm (#1535).</summary>
    internal static bool HasQueuedCandidate(IReadOnlyList<AccountExplorerCandidateReadModel> candidates)
        => candidates.Any(c => c.Status == nameof(MainCandidateStatus.Queued));

    /// <summary>
    /// The other membership arm of the real ingest claim: a <c>Queued</c> candidate on an
    /// account never ingested. An ingested account is the established-main arm's (#1535).
    /// </summary>
    internal static bool IsInNewCandidateArm(
        RiotAccount account,
        IReadOnlyList<AccountExplorerCandidateReadModel> candidates)
        => account.LastMatchIngestAtUtc is null && HasQueuedCandidate(candidates);

    internal static string DescribeState(
        AccountPipelineState state,
        RiotAccount account,
        IReadOnlyList<AccountExplorerCandidateReadModel> candidates,
        IReadOnlyList<AccountExplorerMainRowReadModel> mainRows,
        AccountExplorerMatchesIngestedReadModel matches)
        => state switch
        {
            AccountPipelineState.Invalidated =>
                "account-v1 no longer resolves this PUUID and AccountRefresh could not recover it by Riot "
                + "ID, so the row is marked Invalid. It is kept for history but excluded from every refresh "
                + "and ingest selection: nothing downstream will move again until the account is re-seeded.",

            AccountPipelineState.Tracked =>
                $"In the match-ingestion population, with {matches.LiveParticipantCount} participant row(s) "
                + $"currently on disk and {mainRows.Count(m => m is { IsMain: true, IsActive: true })} active "
                + "main(s). "
                + (account.LastMatchIngestAtUtc is null
                    ? "Its lease has never come up, so no games have been fetched yet."
                    : $"Last ingested {Format(account.LastMatchIngestAtUtc.Value)}."),

            AccountPipelineState.Retired =>
                $"MainActivity has retired every one of this account's {mainRows.Count(m => m.IsMain)} main "
                + "row(s): they are flagged inactive rather than deleted, so the account drops off the site "
                + "and stops consuming match-v5 calls while its history stays readable. Playing the champion "
                + "again reactivates the row without a fresh discovery.",

            AccountPipelineState.NotAMain =>
                $"MainAnalysis has written {mainRows.Count} champion row(s) for this account but promoted "
                + "none of them past the adaptive IsMain floor, so the account is analysed and simply is not "
                + "a main of anything. The play rates and the threshold band below say by how much.",

            AccountPipelineState.CandidateOnly =>
                $"Known to the candidate funnel ({candidates.Count} row(s), status "
                + $"{DescribeStatuses(candidates)}) but never analysed: main_champion_stats holds nothing "
                + "for it. "
                + (account.LastMainCalcAtUtc is null
                    ? "MainAnalysis has never run on this account."
                    : $"MainAnalysis last ran on it {Format(account.LastMainCalcAtUtc.Value)}."),

            AccountPipelineState.Discovered =>
                "The account exists and nothing else has happened to it: no candidate row, no analysed "
                + "champion, and neither membership arm of the ingest claim matches — so it is never "
                + "selected for match ingestion.",

            _ => string.Empty
        };

    /// <summary>
    /// Decides whether retention has demonstrably deleted this account's games,
    /// and says so in words either way. The detection only works through the
    /// frozen aggregates, which cover main champions alone — so a negative is
    /// "no pruning is detectable", never "nothing was pruned".
    /// </summary>
    internal static (bool Pruned, string Note) EvaluatePruning(
        long liveCount,
        long careerGames,
        int patchCount,
        DateTime? oldestRetained,
        DateTime? oldestAggregated)
    {
        const string BlindSpot =
            "A negative here is not proof: the frozen aggregates only ever folded main champions, so "
            + "games on other champions can be deleted without leaving anything to detect them by.";

        if (careerGames == 0)
        {
            return liveCount == 0
                ? (false, "Nothing has been ingested for this account and no frozen aggregate exists, so "
                          + "this is an absence of data rather than a deletion. " + BlindSpot)
                : (false, $"{liveCount} participant row(s) are on disk and no frozen aggregate exists to "
                          + "compare them against, so nothing can be said about deletions. " + BlindSpot);
        }

        if (liveCount == 0)
        {
            return (true, $"Retention has deleted this account's games: the frozen aggregates account for "
                          + $"{careerGames} game(s) across {patchCount} patch(es), and no participant row "
                          + "survives. The zero above is a storage window, not a play history.");
        }

        if (oldestAggregated is not null && oldestRetained is not null && oldestAggregated < oldestRetained)
        {
            return (true, $"Retention has deleted part of this account's history: the frozen aggregates "
                          + $"reach back to at least {Format(oldestAggregated.Value)}, while the oldest "
                          + $"surviving participant row is from {Format(oldestRetained.Value)}.");
        }

        return (false, $"No deletion is detectable: the frozen aggregates ({careerGames} game(s) over "
                       + $"{patchCount} patch(es)) do not reach further back than the surviving participant "
                       + "rows. " + BlindSpot);
    }

    /// <summary>The one timestamp format every explorer sentence uses.</summary>
    internal static string Format(DateTime value)
        => value.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Every distinct candidate status, in funnel order. Deliberately not a
    /// "furthest status": an account's champions sit at different stages, and one
    /// label would hide the rows still short of Validated.
    /// </summary>
    private static string DescribeStatuses(IReadOnlyList<AccountExplorerCandidateReadModel> candidates)
        => string.Join(", ", candidates
            .Select(c => Enum.TryParse<MainCandidateStatus>(c.Status, out var parsed)
                ? parsed
                : MainCandidateStatus.New)
            .Distinct()
            .OrderBy(status => status)
            .Select(status => status.ToString()));
}
