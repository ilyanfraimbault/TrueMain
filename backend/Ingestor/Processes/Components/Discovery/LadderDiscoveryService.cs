using Core.Lol.Identifiers;
using Ingestor.Options;
using Ingestor.Ranking;
using Ingestor.Riot;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.Discovery;

public sealed class LadderDiscoveryService(IRiotPlatformClient riotPlatformClient) : ILadderDiscoveryService
{
    private const string RankedSoloQueue = "RANKED_SOLO_5x5";

    public async Task<List<DiscoveredSummoner>> DiscoverSummonersAsync(
        PlatformRoute platform,
        DiscoveryOptions options,
        CancellationToken ct)
    {
        var ladderEntries = await FetchLadderEntriesAsync(platform, options, ct);
        var boundedEntries = ladderEntries
            .DistinctBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, options.MaxAccountsPerPlatformPerRun))
            .ToList();

        var discovered = new List<DiscoveredSummoner>(boundedEntries.Count);
        foreach (var entry in boundedEntries)
        {
            ct.ThrowIfCancellationRequested();

            var summoner = await ResolveSummonerAsync(platform, entry, ct);
            if (string.IsNullOrWhiteSpace(summoner.Puuid))
            {
                continue;
            }

            discovered.Add(new DiscoveredSummoner(summoner, entry.Rank));
        }

        return discovered;
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
