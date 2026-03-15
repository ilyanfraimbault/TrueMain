using Data;
using Data.Entities;
using Core.Options;
using Ingestor.Options;
using Ingestor.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public sealed class RawDataRetentionProcess(
    ILogger<RawDataRetentionProcess> logger,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    IProcessRunRecorder runRecorder,
    IOptions<RawDataRetentionOptions> retentionOptions,
    IOptions<MainAnalysisOptions> mainAnalysisOptions)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var retainedPatchCount = Math.Max(1, retentionOptions.Value.RetainedPatchCount);
            var queueId = mainAnalysisOptions.Value.QueueId;

            var observedMatches = await db.Matches
                .AsNoTracking()
                .Where(match => match.QueueId == queueId)
                .OrderByDescending(match => match.GameStartTimeUtc)
                .Select(match => new
                {
                    match.PlatformId,
                    match.GameVersion
                })
                .ToListAsync(ct);

            var retainedPatchesByPlatform = observedMatches
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

            if (retainedPatchesByPlatform.Count == 0)
            {
                await runRecorder.RecordAsync(
                    "RawDataRetention",
                    startedAt,
                    DateTime.UtcNow,
                    ProcessRunStatus.Success,
                    new
                    {
                        retainedPatchCount,
                        queueId,
                        deletedMatches = 0,
                        deletedParticipants = 0,
                        retainedPatchesByPlatform = Array.Empty<object>()
                    },
                    null,
                    ct);
                return;
            }

            var rawMatches = await db.Matches
                .AsNoTracking()
                .Select(match => new
                {
                    match.Id,
                    match.PlatformId,
                    match.QueueId,
                    match.GameVersion
                })
                .ToListAsync(ct);

            var deletableMatchIds = rawMatches
                .Where(match => match.QueueId == queueId
                    && retainedPatchesByPlatform.TryGetValue(match.PlatformId, out var retainedPatches)
                    && !retainedPatches.Contains(NormalizePatchVersion(match.GameVersion)))
                .Select(match => match.Id)
                .ToList();

            if (deletableMatchIds.Count == 0)
            {
                await runRecorder.RecordAsync(
                    "RawDataRetention",
                    startedAt,
                    DateTime.UtcNow,
                    ProcessRunStatus.Success,
                    new
                    {
                        retainedPatchCount,
                        queueId,
                        deletedMatches = 0,
                        deletedParticipants = 0,
                        retainedPatchesByPlatform = retainedPatchesByPlatform
                            .OrderBy(entry => entry.Key)
                            .Select(entry => new
                            {
                                platformId = entry.Key,
                                patches = entry.Value.Order().ToArray()
                            })
                            .ToArray()
                    },
                    null,
                    ct);
                return;
            }

            var deletedParticipants = await db.MatchParticipants
                .Where(participant => deletableMatchIds.Contains(participant.MatchId))
                .ExecuteDeleteAsync(ct);

            var deletedMatches = await db.Matches
                .Where(match => deletableMatchIds.Contains(match.Id))
                .ExecuteDeleteAsync(ct);

            logger.LogInformation(
                "Raw data retention removed {DeletedMatches} matches and {DeletedParticipants} participants while keeping patches {RetainedPatches}.",
                deletedMatches,
                deletedParticipants,
                string.Join(
                    ", ",
                    retainedPatchesByPlatform
                        .OrderBy(entry => entry.Key)
                        .Select(entry => $"{entry.Key}=[{string.Join("|", entry.Value.Order())}]")));

            await runRecorder.RecordAsync(
                "RawDataRetention",
                startedAt,
                DateTime.UtcNow,
                ProcessRunStatus.Success,
                new
                {
                    retainedPatchCount,
                    queueId,
                    deletedMatches,
                    deletedParticipants,
                    retainedPatchesByPlatform = retainedPatchesByPlatform
                        .OrderBy(entry => entry.Key)
                        .Select(entry => new
                        {
                            platformId = entry.Key,
                            patches = entry.Value.Order().ToArray()
                        })
                        .ToArray()
                },
                null,
                ct);
        }
        catch (Exception ex)
        {
            await runRecorder.RecordAsync(
                "RawDataRetention",
                startedAt,
                DateTime.UtcNow,
                ProcessRunStatus.Failed,
                null,
                ex.Message,
                ct);
            throw;
        }
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
}
