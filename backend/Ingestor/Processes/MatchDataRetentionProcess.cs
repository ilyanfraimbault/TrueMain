using Core.Lol.Patches;
using Core.Options;
using Data;
using Ingestor.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public sealed class MatchDataRetentionProcess(
    ILogger<MatchDataRetentionProcess> logger,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    IOptions<MatchDataRetentionOptions> retentionOptions,
    IOptions<MainAnalysisOptions> mainAnalysisOptions,
    IOptions<CandidatePruningOptions> candidatePruningOptions) : IIngestorProcess
{
    public string Name => "MatchDataRetention";

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        // Prune stale never-promoted candidates first (#487) — independent of match
        // retention, so it runs even when there is nothing to delete below.
        var prunedCandidates = await PruneStaleCandidatesAsync(ct);

        var retentionPlan = await LoadRetentionPlanAsync(ct);
        if (retentionPlan.RetainedPatchesByPlatform.Count == 0
            || retentionPlan.DeletableMatchIds.Count == 0)
        {
            return BuildRetentionPayload(retentionPlan, 0, 0, prunedCandidates);
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

        return BuildRetentionPayload(retentionPlan, deletionResult.DeletedMatches, deletionResult.DeletedParticipants, prunedCandidates);
    }

    private async Task<int> PruneStaleCandidatesAsync(CancellationToken ct)
    {
        var options = candidatePruningOptions.Value;
        if (!options.Enabled || options.PruneAfterDays <= 0)
        {
            return 0;
        }

        var cutoffUtc = DateTime.UtcNow - TimeSpan.FromDays(options.PruneAfterDays);
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var pruned = await new Data.Repositories.MainCandidateRepository(db)
            .PruneStaleNeverPromotedAsync(cutoffUtc, ct);

        if (pruned > 0)
        {
            logger.LogInformation(
                "Candidate pruning removed {PrunedCandidates} stale never-promoted candidate(s) inactive since before {Cutoff:o}.",
                pruned,
                cutoffUtc);
        }

        return pruned;
    }

    private async Task<RetentionPlan> LoadRetentionPlanAsync(CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var retainedPatchCount = Math.Max(1, retentionOptions.Value.RetainedPatchCount);
        var queueId = (int)mainAnalysisOptions.Value.QueueId;
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
                    .Select(match => PatchVersion.TryParse(match.GameVersion, out var patch)
                        ? $"{patch.Major}.{patch.Minor}"
                        : null)
                    .Where(patch => !string.IsNullOrWhiteSpace(patch))
                    .Select(patch => patch!)
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
        var deletableMatchIds = new List<string>();

        foreach (var (platformId, retainedPatches) in retainedPatchesByPlatform.OrderBy(entry => entry.Key))
        {
            var platformQuery = db.Matches
                .AsNoTracking()
                .Where(match => match.QueueId == queueId && match.PlatformId == platformId);

            foreach (var retainedPatch in retainedPatches)
            {
                var patchPrefix = $"{retainedPatch}.%";
                platformQuery = platformQuery
                    .Where(match => match.GameVersion != retainedPatch
                        && !EF.Functions.Like(match.GameVersion, patchPrefix));
            }

            deletableMatchIds.AddRange(await platformQuery
                .Select(match => match.Id)
                .ToListAsync(ct));
        }

        return deletableMatchIds;
    }

    private async Task<DeletionResult> DeleteExpiredMatchDataAsync(
        IReadOnlyCollection<string> deletableMatchIds,
        CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var deletedParticipants = await db.MatchParticipants
            .Where(participant => deletableMatchIds.Contains(participant.MatchId))
            .ExecuteDeleteAsync(ct);
        var deletedMatches = await db.Matches
            .Where(match => deletableMatchIds.Contains(match.Id))
            .ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);

        return new DeletionResult(deletedMatches, deletedParticipants);
    }

    private static object BuildRetentionPayload(
        RetentionPlan retentionPlan,
        int deletedMatches,
        int deletedParticipants,
        int prunedCandidates)
    {
        return new
        {
            retainedPatchCount = retentionPlan.RetainedPatchCount,
            queueId = retentionPlan.QueueId,
            deletedMatches,
            deletedParticipants,
            prunedCandidates,
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

    private sealed record ObservedMatch(string PlatformId, string GameVersion);

    private sealed record DeletionResult(int DeletedMatches, int DeletedParticipants);

    private sealed record RetentionPlan(
        int RetainedPatchCount,
        int QueueId,
        IReadOnlyDictionary<string, HashSet<string>> RetainedPatchesByPlatform,
        IReadOnlyList<string> DeletableMatchIds);
}
