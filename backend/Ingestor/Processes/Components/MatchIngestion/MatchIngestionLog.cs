using Data.Logging;

namespace Ingestor.Processes.Components.MatchIngestion;

/// <summary>
/// Match ingestion's log lines, source-generated (#1636): most are written once per claimed
/// account or per match, the pipeline's hottest loop. The two ops events keep their
/// <see cref="OpsEvents"/> id and name, which is what the Mongo sink recognises them by.
/// </summary>
internal static partial class MatchIngestionLog
{
    [LoggerMessage(
        EventId = OpsEvents.Ids.CandidateValidated,
        EventName = nameof(OpsEvents.CandidateValidated),
        Level = LogLevel.Information,
        Message = "Validated {Count} candidates for {Platform}/{Puuid}.")]
    public static partial void CandidateValidated(this ILogger logger, int count, string platform, string puuid);

    [LoggerMessage(
        EventId = OpsEvents.Ids.MatchRevertFailed,
        EventName = nameof(OpsEvents.MatchRevertFailed),
        Level = LogLevel.Error,
        Message = "Failed to revert claim for {Platform}/{Puuid} after ingestion error; "
            + "candidates remain Processing until the claim lease expires.")]
    public static partial void MatchRevertFailed(this ILogger logger, Exception exception, string platform, string puuid);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Released {Count} candidates back to Queued for uningestable {Platform}/{Puuid}.")]
    public static partial void UningestableReleased(this ILogger logger, int count, string platform, string puuid);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Reverted {Count} candidates to Queued for {Platform}/{Puuid}.")]
    public static partial void ClaimReverted(this ILogger logger, int count, string platform, string puuid);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Unknown platform {Platform} on claimed account {Puuid}; releasing the claim without ingesting.")]
    public static partial void UnknownPlatformClaimed(this ILogger logger, string platform, string puuid);

    [LoggerMessage(Level = LogLevel.Error, Message = "Match ingestion failed for {Platform}/{Puuid}. Reverting to queued.")]
    public static partial void AccountIngestionFailed(this ILogger logger, Exception exception, string platform, string puuid);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Match ingestion for {Platform}/{Puuid}: inserted={Inserted}, skipped={Skipped}, "
            + "skippedWrongQueue={SkippedWrongQueue}, timelinesUpdated={Timelines}, mainsReactivated={MainsReactivated}.")]
    public static partial void AccountIngested(
        this ILogger logger,
        string platform,
        string puuid,
        int inserted,
        int skipped,
        int skippedWrongQueue,
        int timelines,
        int mainsReactivated);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Match ingestion summary for {Platform}: accounts={Accounts}, matchesInserted={Inserted}, "
            + "matchesSkipped={Skipped}, matchesSkippedWrongQueue={SkippedWrongQueue}, timelinesUpdated={Timelines}.")]
    public static partial void PlatformIngested(
        this ILogger logger,
        string platform,
        int accounts,
        int inserted,
        int skipped,
        int skippedWrongQueue,
        int timelines);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Released {Candidates} candidate(s) and {Accounts} account claim(s) held before {Cutoff:O}.")]
    public static partial void ExpiredClaimsReleased(this ILogger logger, int candidates, int accounts, DateTime cutoff);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Claim allocation for a batch of {BatchSize} at a quota-weighted establishedMainShare {Share:0.###} "
            + "(configured {ConfiguredShare}): {Quotas}.")]
    public static partial void ClaimAllocated(
        this ILogger logger, int batchSize, double share, double configuredShare, string quotas);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Moved queued candidates to Processing for {CandidateAccountCount} of {ClaimedAccountCount} claimed accounts.")]
    public static partial void CandidatesClaimed(this ILogger logger, int candidateAccountCount, int claimedAccountCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Timeline download failed for {MatchId}; leaving it pending for a later run.")]
    public static partial void TimelineDownloadFailed(this ILogger logger, Exception exception, string matchId);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Pace benchmark: {Unplaced} of {Claimed} match(es) added nothing (remake, no patch, or no ranked tracked account).")]
    public static partial void PaceBenchmarkUnplaced(this ILogger logger, int unplaced, int claimed);
}
