using System.Text.Json;
using AwesomeAssertions;
using Ingestor.Processes.Summaries;

namespace TrueMain.UnitTests;

/// <summary>
/// Process-run summaries are persisted in <c>process_runs.summary</c> and read
/// back by the admin portal, which renders whatever keys the stored JSON carries.
/// Promoting the anonymous payloads to records (#268) therefore had to keep the
/// emitted property names byte-identical.
/// </summary>
/// <remarks>
/// The old recorder called <c>JsonSerializer.Serialize(summary)</c> on a boxed
/// anonymous type with default options — no naming policy, so the wire names were
/// the anonymous members verbatim, and those members were declared in camelCase.
/// Each case below rebuilds the exact anonymous shape the process used to return
/// and serializes it exactly as the old recorder did, so the assertion compares
/// the new output against the real legacy output rather than a transcription of it.
/// </remarks>
public sealed class ProcessRunSummaryJsonTests
{
    [Fact]
    public void EverySummary_SerializesToTheJsonItsAnonymousPredecessorProduced()
    {
        foreach (var (summary, legacyAnonymousShape) in Cases())
        {
            var expected = JsonSerializer.Serialize(legacyAnonymousShape);

            ProcessRunSummaryJson.Serialize(summary)
                .Should().Be(expected, "{0} is a persisted shape", summary.GetType().Name);
        }
    }

    [Fact]
    public void NestedSummaries_KeepTheirExactWireShape()
    {
        // Spelled out rather than compared, so a reviewer can read the persisted
        // shape of the two summaries that nest an array of objects.
        ProcessRunSummaryJson.Serialize(new DiscoverySummary(
            [new DiscoveryPlatformSummary("EUW1", 40, 3, 12, 5, 2, 6, 32, null)]))
            .Should().Be(
                """
                {"platforms":[{"platform":"EUW1","accountsProcessed":40,"newAccounts":3,"candidatesInserted":12,"candidatesUpdated":5,"rankSnapshotsInserted":2,"rankSnapshotsUpdated":6,"rankSnapshotsUnchanged":32,"error":null}]}
                """);

        ProcessRunSummaryJson.Serialize(new MatchDataRetentionSummary(
            3, 420, 10, 100, 4, 7, 42, 5, 900, 1, 2, 4, 5, 8, 9, 6, 11, 3,
            [new RetainedPatchesSummary("KR", ["16.3", "16.4"])]))
            .Should().Be(
                """
                {"retainedPatchCount":3,"queueId":420,"deletedMatches":10,"deletedParticipants":100,"deletedNonRankedMatches":4,"prunedCandidates":7,"demotedQueuedCandidates":42,"prunedSnapshotMatches":5,"deletedIntermediateSnapshots":900,"deletedAggregateScopes":1,"deletedMatchupStats":2,"deletedPowerspikeCurveStats":4,"deletedPowerspikeEventStats":5,"deletedSynergyStats":8,"deletedBanStats":9,"prunedSubFloorPowerspikeEvents":6,"collapsedPowerspikeOpponentShards":11,"collapsedPowerspikeOpponentGroups":3,"retainedPatchesByPlatform":[{"platformId":"KR","patches":["16.3","16.4"]}]}
                """);
    }

    [Fact]
    public void NullFailureReason_StaysAnExplicitNull()
    {
        // The admin's per-platform row reads `error`; dropping the key for a
        // healthy platform (WhenWritingNull) would change the stored shape.
        ProcessRunSummaryJson.Serialize(new DiscoverySummary(
            [new DiscoveryPlatformSummary("KR", 0, 0, 0, 0, 0, 0, 0, null)]))
            .Should().Contain("\"error\":null");
    }

    [Fact]
    public void EverySummaryType_IsRegisteredInTheSourceGeneratedContext()
    {
        // The resolver is source-gen only: an unregistered implementation would
        // throw NotSupportedException on the first run that produced it, in
        // production. Fail here instead.
        var implementations = typeof(IProcessRunSummary).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                && typeof(IProcessRunSummary).IsAssignableFrom(type))
            .ToList();

        implementations.Should().NotBeEmpty();

        foreach (var type in implementations)
        {
            ProcessRunSummaryJson.Options.TryGetTypeInfo(type, out _)
                .Should().BeTrue("{0} must be registered in ProcessRunSummaryJsonContext", type.Name);
        }
    }

    /// <summary>
    /// Each pair is (the record a process returns now, the anonymous object it
    /// returned before #268). The anonymous halves are copied verbatim from the
    /// pre-#268 processes — do not "tidy" their member names.
    /// </summary>
    private static IEnumerable<(IProcessRunSummary Summary, object Legacy)> Cases()
    {
        // ScoringProcess / DiscoveryProcess / MainAnalysisProcess /
        // MatchIngestionProcess / AccountRefreshProcess no-op payload.
        yield return (
            new NoWorkSummary("No platforms configured.", 0),
            new { reason = "No platforms configured.", selected = 0 });

        // DiscoveryProcess cadence skip.
        yield return (
            new SkippedSummary("Within MinRunInterval; discovery skipped this iteration.", true),
            new { reason = "Within MinRunInterval; discovery skipped this iteration.", skipped = true });

        yield return (
            new HarvestNoWorkSummary("No platforms configured.", 0),
            new { reason = "No platforms configured.", candidatesInserted = 0 });

        yield return (
            new ManualSeedNoWorkSummary("No pending seed requests.", 0),
            new { reason = "No pending seed requests.", claimed = 0 });

        yield return (
            new ChampionPatternNoWorkSummary("No champions available for champion pattern aggregation.", 0),
            new { reason = "No champions available for champion pattern aggregation.", patterns = 0 });

        yield return (
            new ScoringSummary([new ScoringPlatformSummary("EUW1", 120, 20)], 45),
            new
            {
                platforms = new List<object>
                {
                    new { platform = "EUW1", scored = 120, queued = 20 }
                },
                promotionCapPerPlatform = 45
            });

        yield return (
            new DiscoverySummary(
            [
                new DiscoveryPlatformSummary("EUW1", 40, 3, 12, 5, 2, 6, 32, null),
                new DiscoveryPlatformSummary("KR", 0, 0, 0, 0, 0, 0, 0, "simulated ladder outage")
            ]),
            new
            {
                platforms = new[]
                {
                    new
                    {
                        platform = "EUW1",
                        accountsProcessed = 40,
                        newAccounts = 3,
                        candidatesInserted = 12,
                        candidatesUpdated = 5,
                        rankSnapshotsInserted = 2,
                        rankSnapshotsUpdated = 6,
                        rankSnapshotsUnchanged = 32,
                        error = (string?)null
                    },
                    new
                    {
                        platform = "KR",
                        accountsProcessed = 0,
                        newAccounts = 0,
                        candidatesInserted = 0,
                        candidatesUpdated = 0,
                        rankSnapshotsInserted = 0,
                        rankSnapshotsUpdated = 0,
                        rankSnapshotsUnchanged = 0,
                        error = (string?)"simulated ladder outage"
                    }
                }
            });

        yield return (
            new MatchIngestionSummary(9, 30, 4, 12, 1, 7, 6, 2,
            [
                new MatchIngestionPlatformSummary("EUW1", 5, 20, 3, 8),
                new MatchIngestionPlatformSummary("KR", 4, 10, 1, 4)
            ]),
            new
            {
                accountsProcessed = 9,
                matchesInserted = 30,
                matchesSkipped = 4,
                timelinesUpdated = 12,
                errors = 1,
                // Appended by #1024, not a rename: every key above keeps its
                // position, so a run recorded before the deploy still reads the
                // same — it simply has no accountsValidated, which is exactly how
                // the funnel tells "not measured yet" from "measured zero".
                accountsValidated = 7,
                // Appended by #1344 for the same reason: a run recorded before the
                // deploy simply has no reap counters, which reads as "not measured"
                // rather than as a run that reaped nothing.
                expiredCandidatesReleased = 6,
                expiredClaimsReleased = 2,
                byPlatform = new[]
                {
                    new
                    {
                        platform = "EUW1",
                        accountsProcessed = 5,
                        matchesInserted = 20,
                        matchesSkipped = 3,
                        timelinesUpdated = 8
                    },
                    new
                    {
                        platform = "KR",
                        accountsProcessed = 4,
                        matchesInserted = 10,
                        matchesSkipped = 1,
                        timelinesUpdated = 4
                    }
                }.ToList()
            });

        yield return (
            new ManualSeedSummary(5, 3, 1, 1, 2),
            new { claimed = 5, ingested = 3, notFound = 1, failed = 1, candidatesQueued = 2 });

        yield return (
            new HarvestSummary(11, 6, 2, 40, 25, 60, 35, true),
            new
            {
                candidatesInserted = 11,
                candidatesUpdated = 6,
                accountsCreated = 2,
                eligibleNew = 40,
                selectedNew = 25,
                eligibleKnown = 60,
                selectedKnown = 35,
                budgetExhausted = true
            });

        yield return (
            new AccountRefreshSummary(50, 30, 2, 1, 10, 3, 20, 8, 25, 4, 6, 2),
            new
            {
                selected = 50,
                profileUpdated = 30,
                profileRecovered = 2,
                profileInvalidated = 1,
                profileSkipped = 10,
                profileFailed = 3,
                rankInserted = 20,
                rankUpdated = 8,
                rankUnchanged = 25,
                rankSkippedUnranked = 4,
                rankSkippedFresh = 6,
                rankFailed = 2
            });

        yield return (
            new MainAnalysisSummary(100, 250, 12, 3),
            new { accountsProcessed = 100, statsUpserted = 250, statsRemoved = 12, demotedAccounts = 3 });

        yield return (
            new ChampionPatternAggregationSummary(9000, 45, 300, 2, 168),
            new { sourceRows = 9000, scopes = 45, patterns = 300, gameVersions = 2, champions = 168 });

        yield return (
            new EloBracketEnrichmentSummary(5000, 40, 2),
            new { stamped = 5000, deferred = 40, batches = 2 });

        yield return (
            new TeamPositionCorrectionSummary(7, 3),
            new { correctedParticipants = 7, inspectedTeams = 3 });

        // ChampionMatchupLeadAggregationProcess and ChampionPowerspikeAggregationProcess.
        yield return (
            new MatchAggregationSummary(4000, 4),
            new { matches = 4000, batches = 4 });

        // ChampionSynergyAggregationProcess (#922) — same match/batch pair plus the
        // two upsert counts, since it writes two tables per fold.
        yield return (
            new SynergyAggregationSummary(4000, 4, 15000, 900),
            new { matches = 4000, batches = 4, pairRows = 15000, baselineRows = 900 });

        // ChampionLaneOutcomeAggregationProcess (#919).
        yield return (
            new LaneOutcomeAggregationSummary(4000, 4, 3600, 900, 300),
            new { matches = 4000, batches = 4, judgedLanes = 3600, rows = 900, goldLeadThreshold = 300 });

        // RunePageDeduplicationProcess (#911).
        yield return (
            new RunePageDeduplicationSummary(20370, 20370, 480000, 28916, 1204, 82),
            new
            {
                groups = 20370,
                deletedPages = 20370,
                repointedPatterns = 480000,
                foldedPatterns = 28916,
                normalizedPages = 1204,
                batches = 82,
            });

        // ChampionBanAggregationProcess (#920) — the synergy shape again, with the
        // champion counts and the (patch, elo band) denominators it wrote.
        yield return (
            new BanAggregationSummary(4000, 4, 12000, 13),
            new { matches = 4000, batches = 4, banRows = 12000, scopeRows = 13 });

        // StorageSnapshotProcess (#925, Mongo counters added by #1023).
        yield return (
            new StorageSnapshotSummary(58, 58, 41_231_686_144, 7, 7, 3_120_508_928),
            new
            {
                tables = 58,
                written = 58,
                databaseBytes = 41_231_686_144L,
                mongoCollections = 7,
                mongoWritten = 7,
                mongoBytes = 3_120_508_928L
            });

        yield return (
            new MatchDataRetentionSummary(3, 420, 10, 100, 4, 7, 42, 5, 900, 1, 2, 4, 5, 8, 9, 6, 11, 3,
            [new RetainedPatchesSummary("KR", ["16.3", "16.4"])]),
            new
            {
                retainedPatchCount = 3,
                queueId = 420,
                deletedMatches = 10,
                deletedParticipants = 100,
                deletedNonRankedMatches = 4,
                prunedCandidates = 7,
                demotedQueuedCandidates = 42,
                prunedSnapshotMatches = 5,
                deletedIntermediateSnapshots = 900,
                deletedAggregateScopes = 1,
                deletedMatchupStats = 2,
                deletedPowerspikeCurveStats = 4,
                deletedPowerspikeEventStats = 5,
                deletedSynergyStats = 8,
                deletedBanStats = 9,
                prunedSubFloorPowerspikeEvents = 6,
                collapsedPowerspikeOpponentShards = 11,
                collapsedPowerspikeOpponentGroups = 3,
                retainedPatchesByPlatform = new[]
                {
                    new { platformId = "KR", patches = new[] { "16.3", "16.4" } }
                }
            });
    }
}
