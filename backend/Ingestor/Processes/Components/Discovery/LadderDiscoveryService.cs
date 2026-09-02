using Core.Lol.Identifiers;
using Ingestor.Options;
using Ingestor.Ranking;
using Ingestor.Riot;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.Discovery;

public sealed class LadderDiscoveryService(IRiotPlatformClient riotPlatformClient) : ILadderDiscoveryService
{
    private const string RankedSoloQueue = "RANKED_SOLO_5x5";

    public async Task<LadderDiscoveryResult> DiscoverSummonersAsync(
        PlatformRoute platform,
        DiscoveryOptions options,
        int offset,
        ProfileFreshnessProbe profileFreshnessProbe,
        CancellationToken ct)
    {
        var ladderEntries = await FetchLadderEntriesAsync(platform, options, ct);
        var distinctEntries = ladderEntries
            .DistinctBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ladderSize = distinctEntries.Count;
        var window = Math.Max(0, options.MaxAccountsPerPlatformPerRun);

        // Sliding window (#486): start the window at the persisted offset and wrap with
        // a modulo so a stale cursor (ladder shrank between runs) can't skip past the end
        // and return nothing. Disabled -> always the top of the ladder (offset 0).
        var appliedOffset = options.SlidingWindowEnabled && ladderSize > 0
            ? offset % ladderSize
            : 0;

        var boundedEntries = distinctEntries
            .Skip(appliedOffset)
            .Take(window)
            .ToList();

        // One query for the whole window instead of a Riot call per entry: every apex entry
        // has carried its PUUID since #1312, so for an account we already store and synced
        // recently, summoner-v4 would return profileIconId / summonerLevel / summonerId and
        // nothing else — cosmetics, worth no request (#1358).
        var freshPuuids = options.ProfileSyncFreshness > TimeSpan.Zero
            ? await profileFreshnessProbe(
                boundedEntries
                    .Select(entry => entry.Puuid)
                    .OfType<string>()
                    .ToList(),
                ct)
            : (IReadOnlySet<string>)new HashSet<string>(StringComparer.Ordinal);

        var discovered = new List<DiscoveredSummoner>(boundedEntries.Count);
        var profileCallsSkipped = 0;
        foreach (var entry in boundedEntries)
        {
            ct.ThrowIfCancellationRequested();

            if (entry.Puuid is { } ladderPuuid && freshPuuids.Contains(ladderPuuid))
            {
                // The ladder entry is the whole payload here: the account exists, and only
                // its rank — which the entry carries — is worth writing this run.
                profileCallsSkipped++;
                discovered.Add(new DiscoveredSummoner(
                    new RiotSummonerDto { Puuid = ladderPuuid },
                    entry.Rank,
                    ProfileResolved: false));
                continue;
            }

            var summoner = await ResolveSummonerAsync(platform, entry, ct);
            if (string.IsNullOrWhiteSpace(summoner.Puuid))
            {
                continue;
            }

            discovered.Add(new DiscoveredSummoner(summoner, entry.Rank));
        }

        return new LadderDiscoveryResult(discovered, ladderSize, appliedOffset, profileCallsSkipped);
    }

    private async Task<List<LadderEntry>> FetchLadderEntriesAsync(
        PlatformRoute platform,
        DiscoveryOptions options,
        CancellationToken ct)
    {
        var tierScope = options.TierScope.Select(tier => tier.Trim().ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
        var result = new List<LadderEntry>();

        if (tierScope.Contains("CHALLENGER"))
        {
            var challenger = await riotPlatformClient.GetChallengerLeagueAsync(platform, RankedSoloQueue, ct);
            result.AddRange(MapEntries(challenger));
        }

        if (tierScope.Contains("GM") || tierScope.Contains("GRANDMASTER"))
        {
            var grandmaster = await riotPlatformClient.GetGrandmasterLeagueAsync(platform, RankedSoloQueue, ct);
            result.AddRange(MapEntries(grandmaster));
        }

        if (tierScope.Contains("MASTER"))
        {
            var master = await riotPlatformClient.GetMasterLeagueAsync(platform, RankedSoloQueue, ct);
            result.AddRange(MapEntries(master));
        }

        return result;
    }

    private static IEnumerable<LadderEntry> MapEntries(RiotLeagueListDto league)
    {
        var tier = league.Tier;
        return league.Entries
            .Select(entry => ToLadderEntry(entry, tier))
            .OfType<LadderEntry>();
    }

    private static LadderEntry? ToLadderEntry(RiotLeagueEntryDto entry, string? tier)
    {
        var hasIdentity = !string.IsNullOrWhiteSpace(entry.SummonerId) || !string.IsNullOrWhiteSpace(entry.Puuid);
        if (!hasIdentity)
        {
            return null;
        }

        var rank = !string.IsNullOrWhiteSpace(tier) && !string.IsNullOrWhiteSpace(entry.Rank)
            ? new RankSnapshotInput(tier!, entry.Rank!, entry.LeaguePoints, entry.Wins, entry.Losses)
            : null;

        return new LadderEntry(
            SummonerId: string.IsNullOrWhiteSpace(entry.SummonerId) ? null : entry.SummonerId,
            Puuid: string.IsNullOrWhiteSpace(entry.Puuid) ? null : entry.Puuid,
            Rank: rank);
    }

    private async Task<RiotSummonerDto> ResolveSummonerAsync(PlatformRoute platform, LadderEntry entry, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(entry.SummonerId))
        {
            return await riotPlatformClient.GetSummonerAsync(platform, entry.SummonerId, ct);
        }

        if (!string.IsNullOrWhiteSpace(entry.Puuid))
        {
            return await riotPlatformClient.GetSummonerByPuuidAsync(platform, entry.Puuid, ct);
        }

        return new RiotSummonerDto();
    }

    private sealed record LadderEntry(string? SummonerId, string? Puuid, RankSnapshotInput? Rank)
    {
        public string Key => SummonerId ?? Puuid ?? string.Empty;
    }
}
