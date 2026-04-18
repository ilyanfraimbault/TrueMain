using Core.Lol.Identifiers;
using Ingestor.Options;
using Ingestor.Riot;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.Discovery;

public sealed class LadderDiscoveryService(IRiotPlatformClient riotPlatformClient) : ILadderDiscoveryService
{
    private const string RankedSoloQueue = "RANKED_SOLO_5x5";

    public async Task<List<RiotSummonerDto>> DiscoverSummonersAsync(
        PlatformRoute platform,
        DiscoveryOptions options,
        CancellationToken ct)
    {
        var ladderEntries = await FetchLadderEntriesAsync(platform, options, ct);
        var boundedEntries = ladderEntries
            .DistinctBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, options.MaxAccountsPerPlatformPerRun))
            .ToList();

        var summoners = new List<RiotSummonerDto>(boundedEntries.Count);
        foreach (var entry in boundedEntries)
        {
            ct.ThrowIfCancellationRequested();

            var summoner = await ResolveSummonerAsync(platform, entry, ct);
            if (string.IsNullOrWhiteSpace(summoner.Puuid))
            {
                continue;
            }

            summoners.Add(summoner);
        }

        return summoners;
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
            result.AddRange(challenger.Entries.Select(ToLadderEntry).OfType<LadderEntry>());
        }

        if (tierScope.Contains("GM") || tierScope.Contains("GRANDMASTER"))
        {
            var grandmaster = await riotPlatformClient.GetGrandmasterLeagueAsync(platform, RankedSoloQueue, ct);
            result.AddRange(grandmaster.Entries.Select(ToLadderEntry).OfType<LadderEntry>());
        }

        if (tierScope.Contains("MASTER"))
        {
            var master = await riotPlatformClient.GetMasterLeagueAsync(platform, RankedSoloQueue, ct);
            result.AddRange(master.Entries.Select(ToLadderEntry).OfType<LadderEntry>());
        }

        return result;
    }

    private static LadderEntry? ToLadderEntry(RiotLeagueEntryDto entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.SummonerId))
        {
            return new LadderEntry(entry.SummonerId, entry.Puuid);
        }

        if (!string.IsNullOrWhiteSpace(entry.Puuid))
        {
            return new LadderEntry(null, entry.Puuid);
        }

        return null;
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

    private sealed record LadderEntry(string? SummonerId, string? Puuid)
    {
        public string Key => SummonerId ?? Puuid ?? string.Empty;
    }
}
