namespace Ingestor.Processes;

/// <summary>
/// The per-account log lines of the account refresh and main activity loops, source-generated
/// (#1636): each pass walks a batch of accounts with one or two Riot calls apiece, and these
/// fire once per account that fails or is skipped.
/// </summary>
internal static partial class AccountLoopLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping riot account {Puuid}: invalid platform {PlatformId}.")]
    public static partial void RefreshInvalidPlatform(this ILogger logger, string puuid, string platformId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed rank refresh for {Platform}/{Puuid}.")]
    public static partial void RankRefreshFailed(this ILogger logger, Exception exception, string platform, string puuid);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Invalidated riot account {Platform}/{Puuid}: unresolvable by PUUID and by Riot ID.")]
    public static partial void AccountInvalidated(this ILogger logger, string platform, string puuid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to refresh riot account {Platform}/{Puuid}.")]
    public static partial void ProfileRefreshFailed(this ILogger logger, Exception exception, string platform, string puuid);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Riot ID recovery lookup failed for {Platform}/{GameName}#{TagLine}; leaving account active.")]
    public static partial void RiotIdRecoveryFailed(
        this ILogger logger, Exception exception, string platform, string? gameName, string? tagLine);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Riot account {Platform}/{Puuid} recovered to PUUID {NewPuuid} already held by another row; "
            + "invalidating the stale duplicate.")]
    public static partial void RecoveredToHeldPuuid(this ILogger logger, string platform, string puuid, string newPuuid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping activity check for {Puuid}: invalid platform {PlatformId}.")]
    public static partial void ActivityInvalidPlatform(this ILogger logger, string puuid, string platformId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed mastery activity check for {Platform}/{Puuid}.")]
    public static partial void ActivityCheckFailed(this ILogger logger, Exception exception, string platform, string puuid);
}
