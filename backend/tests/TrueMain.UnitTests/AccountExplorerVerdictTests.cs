using AwesomeAssertions;
using Data.Entities;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Accounts;

namespace TrueMain.UnitTests;

/// <summary>
/// The account explorer's verdict ladder and pruning detection (#1032, split out in
/// #1526), exercised from hand-built rows — no database involved.
/// </summary>
public sealed class AccountExplorerVerdictTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ResolveState_InvalidAccount_WinsOverAnActiveMain()
    {
        var account = Account(RiotAccountStatus.Invalid);

        var state = AccountExplorerVerdict.ResolveState(account, [MainRow(isMain: true, isActive: true)], []);

        state.Should().Be(AccountPipelineState.Invalidated);
    }

    [Fact]
    public void ResolveState_QueuedCandidateAlone_IsTracked()
    {
        var state = AccountExplorerVerdict.ResolveState(
            Account(), [], [Candidate(MainCandidateStatus.Queued)]);

        state.Should().Be(AccountPipelineState.Tracked);
    }

    [Fact]
    public void ResolveState_OnlyInactiveMainRows_IsRetired()
    {
        var state = AccountExplorerVerdict.ResolveState(
            Account(), [MainRow(isMain: true, isActive: false)], []);

        state.Should().Be(AccountPipelineState.Retired);
    }

    [Fact]
    public void ResolveState_AnalysedButNoMain_IsNotAMain()
    {
        var state = AccountExplorerVerdict.ResolveState(
            Account(), [MainRow(isMain: false, isActive: true)], [Candidate(MainCandidateStatus.Validated)]);

        state.Should().Be(AccountPipelineState.NotAMain);
    }

    [Fact]
    public void ResolveState_CandidatesWithoutRows_IsCandidateOnly_AndNothingIsDiscovered()
    {
        AccountExplorerVerdict.ResolveState(Account(), [], [Candidate(MainCandidateStatus.Scored)])
            .Should().Be(AccountPipelineState.CandidateOnly);
        AccountExplorerVerdict.ResolveState(Account(), [], [])
            .Should().Be(AccountPipelineState.Discovered);
    }

    [Fact]
    public void DescribeState_CandidateOnly_ListsStatusesInFunnelOrder()
    {
        var candidates = new[]
        {
            Candidate(MainCandidateStatus.Rejected),
            Candidate(MainCandidateStatus.Scored),
            Candidate(MainCandidateStatus.Scored)
        };

        var detail = AccountExplorerVerdict.DescribeState(
            AccountPipelineState.CandidateOnly, Account(), candidates, [], new AccountExplorerMatchesIngestedReadModel());

        detail.Should().Contain("(3 row(s), status Scored, Rejected)");
        detail.Should().EndWith("MainAnalysis has never run on this account.");
    }

    [Fact]
    public void EvaluatePruning_NoLiveRowsButAggregates_IsPruned()
    {
        var (pruned, note) = AccountExplorerVerdict.EvaluatePruning(0, 40, 3, null, Now);

        pruned.Should().BeTrue();
        note.Should().Contain("40 game(s) across 3 patch(es)");
    }

    [Fact]
    public void EvaluatePruning_AggregatesReachFurtherBack_IsPruned()
    {
        var (pruned, note) = AccountExplorerVerdict.EvaluatePruning(
            10, 40, 3, oldestRetained: Now, oldestAggregated: Now.AddDays(-30));

        pruned.Should().BeTrue();
        note.Should().Contain("2026-08-02 12:00 UTC").And.Contain("2026-09-01 12:00 UTC");
    }

    [Fact]
    public void EvaluatePruning_NoAggregates_IsNeverAProof()
    {
        var (pruned, note) = AccountExplorerVerdict.EvaluatePruning(5, 0, 0, Now, null);

        pruned.Should().BeFalse();
        note.Should().Contain("A negative here is not proof");
    }

    private static RiotAccount Account(RiotAccountStatus status = RiotAccountStatus.Active)
        => new() { Puuid = "puuid", PlatformId = "EUW1", Status = status };

    private static AccountExplorerMainRowReadModel MainRow(bool isMain, bool isActive)
        => new() { ChampionId = 1, IsMain = isMain, IsActive = isActive };

    private static AccountExplorerCandidateReadModel Candidate(MainCandidateStatus status)
        => new() { ChampionId = 1, Status = status.ToString() };
}
