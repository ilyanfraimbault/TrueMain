using AwesomeAssertions;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Processes.Summaries;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// A permanent fetch worker runs the unchanged match-ingestion step narrowed to its routing
/// value's platforms (#1457). Narrowed, the run claims and reaps those platforms alone — the
/// other workers' claims are live — and unrestricted it is the lane step it always was.
/// </summary>
public sealed class MatchIngestionProcessScopeTests
{
    [Fact]
    public async Task RunCoreAsync_Restricted_ClaimsAndReapsOnlyItsPlatformsWithItsBatchSize()
    {
        var claims = EmptyClaims();
        var scope = new MatchIngestionScope();
        scope.Restrict(["euw1", "EUN1"], 9);

        var summary = await BuildProcess(claims, scope).RunCoreAsync(CancellationToken.None);

        summary.Should().BeOfType<MatchIngestionSummary>().Which.AccountsProcessed.Should().Be(0);
        await claims.Received(1).ClaimAsync(
            Arg.Is<IReadOnlyCollection<string>>(platforms => platforms.SequenceEqual(new[] { "EUW1", "EUN1" })),
            9,
            Arg.Any<double>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
        await claims.Received(1).ReleaseExpiredClaimsAsync(
            Arg.Any<TimeSpan>(),
            Arg.Is<IReadOnlyCollection<string>>(platforms => platforms.SequenceEqual(new[] { "EUW1", "EUN1" })),
            Arg.Any<CancellationToken>());
        await claims.DidNotReceive().ReleaseExpiredClaimsAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCoreAsync_Unrestricted_IsTheLaneStepItAlwaysWas()
    {
        var claims = EmptyClaims();

        await BuildProcess(claims, new MatchIngestionScope()).RunCoreAsync(CancellationToken.None);

        await claims.Received(1).ClaimAsync(
            Arg.Is<IReadOnlyCollection<string>>(platforms => platforms.SequenceEqual(new[] { "EUW1", "KR", "NA1" })),
            75,
            Arg.Any<double>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
        await claims.Received(1).ReleaseExpiredClaimsAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    private static IMatchClaimService EmptyClaims()
    {
        var claims = Substitute.For<IMatchClaimService>();
        claims.ClaimAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<int>(),
                Arg.Any<double>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<AccountKey>());
        return claims;
    }

    private static MatchIngestionProcess BuildProcess(IMatchClaimService claims, MatchIngestionScope scope)
        => new(
            NullLogger<MatchIngestionProcess>.Instance,
            Substitute.For<IDataSessionFactory>(),
            claims,
            Substitute.For<IMatchSnapshotWriter>(),
            Substitute.For<ITimelineIngestionService>(),
            Substitute.For<IAccountValidationService>(),
            Microsoft.Extensions.Options.Options.Create(new MatchIngestionOptions
            {
                Platforms = ["EUW1", "KR", "NA1"],
                BatchSize = 75
            }),
            scope);
}
