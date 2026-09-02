using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class MatchParticipantRepository(TrueMainDbContext db) : IMatchParticipantRepository
{
    private sealed record ParticipantHistoryRow(
        string Puuid,
        int ChampionId,
        string TeamPosition);

    /// <summary>
    /// <see cref="HarvestedCandidateRow"/> plus the per-class size the harvest query
    /// carries on every row (#495). Kept private: the class total is a scan detail that
    /// is folded into <see cref="HarvestPlatformEligibility"/> before leaving the repository.
    /// </summary>
    private sealed record HarvestCandidateScanRow(
        string PlatformId,
        string Puuid,
        int ChampionId,
        int ObservedGames,
        int ObservedWins,
        DateTime LastSeenUtc,
        bool IsKnownCandidate,
        int BucketTotal);

    public Task<List<MatchParticipant>> GetByMatchIdAsync(string matchId, CancellationToken ct)
        => db.MatchParticipants.Where(p => p.MatchId == matchId).ToListAsync(ct);

    public Task<List<MatchParticipant>> GetByMatchIdsAsync(IReadOnlyCollection<string> matchIds, CancellationToken ct)
    {
        if (matchIds.Count == 0)
        {
            return Task.FromResult(new List<MatchParticipant>());
        }

        return db.MatchParticipants
            .Where(participant => matchIds.Contains(participant.MatchId))
            .ToListAsync(ct);
    }

    public Task<int> BackfillRiotAccountIdAsync(
        IReadOnlyCollection<string> matchIds,
        string puuid,
        Guid riotAccountId,
        CancellationToken ct)
    {
        if (matchIds.Count == 0)
        {
            return Task.FromResult(0);
        }

        return db.MatchParticipants
            .Where(participant =>
                matchIds.Contains(participant.MatchId) &&
                participant.RiotAccountId == null &&
                participant.Puuid == puuid)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(participant => participant.RiotAccountId, riotAccountId),
                ct);
    }

    public Task<List<ParticipantRow>> GetRecentParticipantsAsync(string platformId, string puuid, int queueId, int take, CancellationToken ct)
    {
        return (
                from participant in db.MatchParticipants.AsNoTracking()
                join match in db.Matches.AsNoTracking() on participant.MatchId equals match.Id
                where participant.Puuid == puuid &&
                      match.PlatformId == platformId &&
                      match.QueueId == queueId
                orderby match.GameStartTimeUtc descending
                select new ParticipantRow(participant.ChampionId, participant.TeamPosition)
            )
            .Take(Math.Max(1, take))
            .ToListAsync(ct);
    }

    public async Task<Dictionary<AccountKey, List<ParticipantRow>>> GetRecentParticipantsByAccountsAsync(
        IReadOnlyCollection<AccountKey> accounts,
        int queueId,
        int take,
        CancellationToken ct)
    {
        var result = new Dictionary<AccountKey, List<ParticipantRow>>();
        if (accounts.Count == 0)
        {
            return result;
        }

        var safeTake = Math.Max(1, take);
        foreach (var grouping in accounts
                     .Distinct()
                     .GroupBy(account => account.PlatformId.ToUpperInvariant(), StringComparer.Ordinal))
        {
            var normalizedPlatformId = grouping.Key;
            var accountKeysByPuuid = grouping
                .GroupBy(account => account.Puuid, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var puuids = grouping
                .Select(account => account.Puuid)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var participantRows = await db.Database
                .SqlQuery<ParticipantHistoryRow>(
                    $"""
                    SELECT ranked."Puuid", ranked."ChampionId", ranked."TeamPosition"
                    FROM (
                        SELECT
                            p."Puuid",
                            p."ChampionId",
                            p."TeamPosition",
                            m."GameStartTimeUtc",
                            ROW_NUMBER() OVER (
                                PARTITION BY p."Puuid"
                                ORDER BY m."GameStartTimeUtc" DESC
                            ) AS row_num
                        FROM "match_participants" AS p
                        INNER JOIN "matches" AS m ON p."MatchId" = m."Id"
                        WHERE p."Puuid" = ANY ({puuids})
                          AND m."PlatformId" = {normalizedPlatformId}
                          AND m."QueueId" = {queueId}
                    ) AS ranked
                    WHERE ranked.row_num <= {safeTake}
                    ORDER BY ranked."Puuid", ranked."GameStartTimeUtc" DESC
                    """)
                .ToListAsync(ct);

            foreach (var accountRows in participantRows.GroupBy(row => row.Puuid, StringComparer.Ordinal))
            {
                if (!accountKeysByPuuid.TryGetValue(accountRows.Key, out var accountKey))
                {
                    continue;
                }

                result[accountKey] = accountRows
                    .Select(row => new ParticipantRow(row.ChampionId, row.TeamPosition))
                    .ToList();
            }
        }

        return result;
    }

    public async Task<HarvestCandidateBatch> GetHarvestCandidatesAsync(
        IReadOnlyCollection<string> platformIds,
        int queueId,
        int minObservedGames,
        int maxRowsPerBucket,
        DateTime sinceUtc,
        CancellationToken ct)
    {
        var normalizedPlatforms = platformIds
            .Where(platform => !string.IsNullOrWhiteSpace(platform))
            .Select(platform => platform.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedPlatforms.Length == 0)
        {
            return HarvestCandidateBatch.Empty;
        }

        var safeMinGames = Math.Max(1, minObservedGames);
        var safeMaxRows = Math.Max(1, maxRowsPerBucket);
        var harvestSource = (int)MainCandidateSource.Harvest;
        var rejectedStatus = (int)MainCandidateStatus.Rejected;
        var queuedStatus = (int)MainCandidateStatus.Queued;

        // Aggregate one platform at a time instead of PlatformId = ANY(...) (#632). The
        // cross-platform statement hash-aggregated the orphan rows of the whole live
        // match_participants table in a single command; with parallel query disabled
        // (max_parallel_workers_per_gather=0, #589) that ran single-threaded and outgrew
        // Command Timeout=300 as orphan volume climbed. Chunking per platform keeps each
        // command's scope — and therefore its runtime — bounded, mirroring the per-champion
        // chunking of #601. The durable complement is a partial index on the orphan scan
        // (#498), built CONCURRENTLY out-of-band.
        //
        // Each per-platform query returns that platform's own top safeMaxRows PER CLASS
        // (new / known, #495); the union is re-ordered and budgeted by the caller. Any row
        // in a global top-N of one class is necessarily within its own platform's top-N of
        // that class (at most N-1 rows outrank it globally, hence at most N-1 within its
        // platform), so the per-platform slices stay a superset of the caller's selection.
        var merged = new List<HarvestedCandidateRow>(safeMaxRows * normalizedPlatforms.Length * 2);
        var eligibility = new List<HarvestPlatformEligibility>(normalizedPlatforms.Length);
        foreach (var platform in normalizedPlatforms)
        {
            // `observed`: index-friendly GROUP BY over orphan participant rows (RiotAccountId
            // IS NULL = untracked players). The GameStartTimeUtc >= sinceUtc predicate bounds
            // the scan explicitly (a caller-supplied lookback) instead of relying on
            // MatchDataRetention having physically deleted older rows. The (Puuid, MatchId)
            // index supports the join. SUM over the bool Win column needs an explicit CASE for
            // Postgres. PlatformId is constant here but kept in the projection/GROUP BY so the
            // row shape is unchanged.
            //
            // `classified`: anti-starvation split (#495). Ordering the whole pool by observed
            // games and cutting at the cap hands every slot to the most-observed players, who
            // are already candidates — a pair that just crossed MinObservedGames would never
            // reach the window once the pool outgrows the cap. Tagging each pair with whether
            // it already has a candidate lets the caller budget discovery separately from
            // refresh. The join is on the unique (PlatformId, Puuid, ChampionId) index, with
            // the platform pinned to the literal (equal to o.platform_id here) so the planner
            // gets a sargable leading-column predicate. Pairs whose candidate exists but is
            // NOT refreshable are dropped from both classes: a ladder/manual-seed candidate is
            // left untouched by the harvest on purpose (observed stats stay 0 outside Harvest),
            // a Rejected one must not be resurrected, and a Queued one is already past scoring
            // (#1361) — refreshing its observed stats rewrites a row whose score nothing will
            // read again before the claim reaches it, which is what made this the pipeline's
            // single largest write source. Returning any of them would only burn budget on a
            // no-op.
            //
            // `ranked`: rank within each class and carry the class's exact size. The window
            // COUNT is computed before the LIMIT, so the caller can report what it dropped
            // instead of truncating silently. The puuid/champion tiebreak keeps the cut
            // deterministic when observed games and last-seen collide.
            var scanRows = await db.Database
                .SqlQuery<HarvestCandidateScanRow>(
                    $"""
                    WITH observed AS (
                        SELECT
                            m."PlatformId" AS platform_id,
                            p."Puuid" AS puuid,
                            p."ChampionId" AS champion_id,
                            COUNT(*)::int AS observed_games,
                            SUM(CASE WHEN p."Win" THEN 1 ELSE 0 END)::int AS observed_wins,
                            MAX(m."GameStartTimeUtc") AS last_seen_utc
                        FROM "match_participants" AS p
                        INNER JOIN "matches" AS m ON p."MatchId" = m."Id"
                        WHERE p."RiotAccountId" IS NULL
                          AND m."PlatformId" = {platform}
                          AND m."QueueId" = {queueId}
                          AND m."GameStartTimeUtc" >= {sinceUtc}
                        GROUP BY m."PlatformId", p."Puuid", p."ChampionId"
                        HAVING COUNT(*) >= {safeMinGames}
                    ),
                    classified AS (
                        SELECT
                            o.*,
                            c."Id" IS NOT NULL AS is_known
                        FROM observed AS o
                        LEFT JOIN "main_candidates" AS c
                            ON c."PlatformId" = {platform}
                           AND c."Puuid" = o.puuid
                           AND c."ChampionId" = o.champion_id
                        WHERE c."Id" IS NULL
                           OR (c."Source" = {harvestSource}
                               AND c."Status" <> {rejectedStatus}
                               AND c."Status" <> {queuedStatus})
                    ),
                    ranked AS (
                        SELECT
                            c.*,
                            ROW_NUMBER() OVER (
                                PARTITION BY c.is_known
                                ORDER BY c.observed_games DESC, c.last_seen_utc DESC, c.puuid, c.champion_id
                            ) AS bucket_rank,
                            COUNT(*) OVER (PARTITION BY c.is_known)::int AS bucket_total
                        FROM classified AS c
                    )
                    SELECT
                        r.platform_id AS "PlatformId",
                        r.puuid AS "Puuid",
                        r.champion_id AS "ChampionId",
                        r.observed_games AS "ObservedGames",
                        r.observed_wins AS "ObservedWins",
                        r.last_seen_utc AS "LastSeenUtc",
                        r.is_known AS "IsKnownCandidate",
                        r.bucket_total AS "BucketTotal"
                    FROM ranked AS r
                    WHERE r.bucket_rank <= {safeMaxRows}
                    ORDER BY r.is_known, r.bucket_rank
                    """)
                .ToListAsync(ct);

            // Every row of a class carries that class's total, so the first row of each is
            // enough; an empty class is genuinely empty (safeMaxRows >= 1 always returns a
            // row when the class has any).
            eligibility.Add(new HarvestPlatformEligibility(
                platform,
                scanRows.FirstOrDefault(row => !row.IsKnownCandidate)?.BucketTotal ?? 0,
                scanRows.FirstOrDefault(row => row.IsKnownCandidate)?.BucketTotal ?? 0));

            merged.AddRange(scanRows.Select(row => new HarvestedCandidateRow(
                row.PlatformId,
                row.Puuid,
                row.ChampionId,
                row.ObservedGames,
                row.ObservedWins,
                row.LastSeenUtc,
                row.IsKnownCandidate)));
        }

        return new HarvestCandidateBatch(merged, eligibility);
    }

    public void AddRange(IEnumerable<MatchParticipant> participants)
        => db.MatchParticipants.AddRange(participants);

    public async Task<Dictionary<PerkCatalogKey, int>> GetOrCreatePerkCatalogIdsAsync(
        IReadOnlyCollection<PerkCatalogKey> keys,
        CancellationToken ct)
    {
        var distinctKeys = keys
            .Distinct()
            .ToArray();

        if (distinctKeys.Length == 0)
        {
            return [];
        }

        var existingMap = await LoadCatalogIdsByKeysAsync(distinctKeys, ct);
        var missingKeys = distinctKeys
            .Where(key => !existingMap.ContainsKey(key))
            .ToArray();

        if (missingKeys.Length == 0)
        {
            return existingMap;
        }

        // Insert the missing rows with a raw ON CONFLICT DO NOTHING so the unique
        // index collisions that occur under concurrent ingestion are absorbed by
        // Postgres instead of surfacing as a DbUpdateException. This avoids the
        // previous catch + ChangeTracker.Clear() recovery, which silently discarded
        // any pending changes the caller had staged on this context (a lost-update
        // anti-pattern). The insert never touches the change tracker; IDs are
        // reloaded by key below for both pre-existing and freshly inserted rows.
        var styleIds = new int[missingKeys.Length];
        var selectionIndexes = new int[missingKeys.Length];
        var perkIds = new int[missingKeys.Length];
        var styleDescriptions = new string[missingKeys.Length];
        for (var i = 0; i < missingKeys.Length; i++)
        {
            var key = missingKeys[i];
            styleIds[i] = key.StyleId;
            selectionIndexes[i] = key.SelectionIndex;
            perkIds[i] = key.PerkId;
            styleDescriptions[i] = key.StyleDescription;
        }

        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "perk_selection_catalog"
                ("StyleId", "SelectionIndex", "PerkId", "StyleDescription")
            SELECT * FROM unnest(
                {styleIds},
                {selectionIndexes},
                {perkIds},
                {styleDescriptions})
            ON CONFLICT ("StyleId", "SelectionIndex", "PerkId", "StyleDescription") DO NOTHING
            """,
            ct);

        return await LoadCatalogIdsByKeysAsync(distinctKeys, ct);
    }

    public void AddPerkSelections(IEnumerable<ParticipantPerkSelection> selections)
        => db.ParticipantPerkSelections.AddRange(selections);

    private async Task<Dictionary<PerkCatalogKey, int>> LoadCatalogIdsByKeysAsync(
        IReadOnlyCollection<PerkCatalogKey> keys,
        CancellationToken ct)
    {
        var map = new Dictionary<PerkCatalogKey, int>();
        var styleIds = keys.Select(key => key.StyleId).Distinct().ToArray();

        var catalogs = await db.PerkSelectionCatalogs
            .AsNoTracking()
            .Where(catalog => styleIds.Contains(catalog.StyleId))
            .ToListAsync(ct);

        foreach (var catalog in catalogs)
        {
            var key = new PerkCatalogKey(
                catalog.StyleId,
                catalog.SelectionIndex,
                catalog.PerkId,
                catalog.StyleDescription);
            map.TryAdd(key, catalog.Id);
        }

        return keys
            .Where(key => map.ContainsKey(key))
            .ToDictionary(key => key, key => map[key]);
    }
}
