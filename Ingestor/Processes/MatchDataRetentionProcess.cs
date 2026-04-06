using Data;
using Data.Entities;
using Core.Options;
using Ingestor.Options;
using Ingestor.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public sealed class MatchDataRetentionProcess(
    ILogger<MatchDataRetentionProcess> logger,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    IProcessRunRecorder runRecorder,
    IOptions<MatchDataRetentionOptions> retentionOptions,
    IOptions<MainAnalysisOptions> mainAnalysisOptions)
{
    private const string ProcessName = "MatchDataRetention";

    public async Task RunAsync(CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        try
        {
            var retentionPlan = await LoadRetentionPlanAsync(ct);
            if (retentionPlan.RetainedPatchesByPlatform.Count == 0)
            {
                await runRecorder.RecordNoOpAsync(
                    ProcessName,
                    startedAt,
                    BuildRetentionPayload(retentionPlan, 0, 0),
                    ct);
                return;
            }

            if (retentionPlan.DeletableMatchIds.Count == 0)
            {
                await runRecorder.RecordSuccessAsync(ProcessName, startedAt, BuildRetentionPayload(retentionPlan, 0, 0), ct);
                return;
            }

            var deletionResult = await DeleteExpiredMatchDataAsync(retentionPlan.DeletableMatchIds, ct);
            logger.LogInformation(
                "Match data retention removed {DeletedMatches} matches and {DeletedParticipants} participants while keeping patches {RetainedPatches}.",
                deletionResult.DeletedMatches,
                deletionResult.DeletedParticipants,
                string.Join(
                    ", ",
                    retentionPlan.RetainedPatchesByPlatform
                        .OrderBy(entry => entry.Key)
                        .Select(entry => $"{entry.Key}=[{string.Join("|", entry.Value.Order())}]")));

            await runRecorder.RecordSuccessAsync(
                ProcessName,
                startedAt,
                BuildRetentionPayload(retentionPlan, deletionResult.DeletedMatches, deletionResult.DeletedParticipants),
                ct);
        }
        catch (Exception ex)
        {
            await runRecorder.RecordFailureAsync(ProcessName, startedAt, ex, ct);
            throw;
        }
    }

    private async Task<RetentionPlan> LoadRetentionPlanAsync(CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var retainedPatchCount = Math.Max(1, retentionOptions.Value.RetainedPatchCount);
        var queueId = mainAnalysisOptions.Value.QueueId;
        var observedMatches = await LoadObservedPatchesAsync(db, queueId, ct);
        var retainedPatchesByPlatform = ComputeRetainedPatchesByPlatform(observedMatches, retainedPatchCount);
        var deletableMatchIds = retainedPatchesByPlatform.Count == 0
            ? []
            : await FindDeletableMatchIdsAsync(db, queueId, retainedPatchesByPlatform, ct);

        return new RetentionPlan(retainedPatchCount, queueId, retainedPatchesByPlatform, deletableMatchIds);
    }

    private static Task<List<ObservedMatch>> LoadObservedPatchesAsync(
        TrueMainDbContext db,
        int queueId,
        CancellationToken ct)
    {
        return db.Matches
            .AsNoTracking()
            .Where(match => match.QueueId == queueId)
            .OrderByDescending(match => match.GameStartTimeUtc)
            .Select(match => new ObservedMatch(match.PlatformId, match.GameVersion))
            .ToListAsync(ct);
    }

    private static Dictionary<string, HashSet<string>> ComputeRetainedPatchesByPlatform(
        IReadOnlyCollection<ObservedMatch> observedMatches,
        int retainedPatchCount)
    {
        return observedMatches
            .GroupBy(match => match.PlatformId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(match => NormalizePatchVersion(match.GameVersion))
                    .Where(patch => !string.IsNullOrWhiteSpace(patch))
                    .Distinct()
                    .Take(retainedPatchCount)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    private static async Task<List<string>> FindDeletableMatchIdsAsync(
        TrueMainDbContext db,
        int queueId,
        IReadOnlyDictionary<string, HashSet<string>> retainedPatchesByPlatform,
        CancellationToken ct)
    {
        var rawMatches = await db.Matches
            .AsNoTracking()
            .Select(match => new MatchIdentity(match.Id, match.PlatformId, match.QueueId, match.GameVersion))
            .ToListAsync(ct);

        return rawMatches
            .Where(match => match.QueueId == queueId
                && retainedPatchesByPlatform.TryGetValue(match.PlatformId, out var retainedPatches)
                && !retainedPatches.Contains(NormalizePatchVersion(match.GameVersion)))
            .Select(match => match.Id)
            .ToList();
    }

    private async Task<DeletionResult> DeleteExpiredMatchDataAsync(
        IReadOnlyCollection<string> deletableMatchIds,
        CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var deletedParticipants = await db.MatchParticipants
            .Where(participant => deletableMatchIds.Contains(participant.MatchId))
            .ExecuteDeleteAsync(ct);
        var deletedMatches = await db.Matches
            .Where(match => deletableMatchIds.Contains(match.Id))
            .ExecuteDeleteAsync(ct);

        return new DeletionResult(deletedMatches, deletedParticipants);
    }

    private static object BuildRetentionPayload(
        RetentionPlan retentionPlan,
        int deletedMatches,
        int deletedParticipants)
    {
        return new
        {
            retainedPatchCount = retentionPlan.RetainedPatchCount,
            queueId = retentionPlan.QueueId,
            deletedMatches,
            deletedParticipants,
            retainedPatchesByPlatform = retentionPlan.RetainedPatchesByPlatform
                .OrderBy(entry => entry.Key)
                .Select(entry => new
                {
                    platformId = entry.Key,
                    patches = entry.Value.Order().ToArray()
                })
                .ToArray()
        };
    }

    private static string NormalizePatchVersion(string gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            return string.Empty;
        }

        var segments = gameVersion.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length >= 2
            ? $"{segments[0]}.{segments[1]}"
            : gameVersion;
    }

    private sealed record ObservedMatch(string PlatformId, string GameVersion);

    private sealed record MatchIdentity(string Id, string PlatformId, int QueueId, string GameVersion);

    private sealed record DeletionResult(int DeletedMatches, int DeletedParticipants);

    private sealed record RetentionPlan(
        int RetainedPatchCount,
        int QueueId,
        IReadOnlyDictionary<string, HashSet<string>> RetainedPatchesByPlatform,
        IReadOnlyList<string> DeletableMatchIds);
}
