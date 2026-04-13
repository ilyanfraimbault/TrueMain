using Core.Options;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public sealed class ChampionFoundationQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options) : IChampionFoundationQueryService
{
    public async Task<ChampionFoundationReadModel?> GetAsync(
        int championId,
        Guid? riotAccountId,
        string? patch,
        string? platformId,
        string? position,
        CancellationToken ct)
    {
        var query = db.ChampionPatternAggregates
            .AsNoTracking()
            .Where(aggregate => aggregate.ChampionId == championId && aggregate.QueueId == options.Value.QueueId);

        if (riotAccountId.HasValue)
        {
            query = query.Where(aggregate => aggregate.RiotAccountId == riotAccountId.Value);
        }

        if (!string.IsNullOrWhiteSpace(platformId))
        {
            query = query.Where(aggregate => aggregate.PlatformId == platformId);
        }

        if (!string.IsNullOrWhiteSpace(position))
        {
            query = query.Where(aggregate => aggregate.Position == position);
        }

        var aggregateRows = await query.ToListAsync(ct);

        if (aggregateRows.Count == 0)
        {
            return null;
        }

        var selectedPatchVersion = string.IsNullOrWhiteSpace(patch)
            ? aggregateRows
                .Select(aggregate => aggregate.GameVersion)
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(ParsePatchVersion)
                .FirstOrDefault()
            : patch;

        if (string.IsNullOrWhiteSpace(selectedPatchVersion))
        {
            return null;
        }

        var patchRows = aggregateRows
            .Where(aggregate => string.Equals(aggregate.GameVersion, selectedPatchVersion, StringComparison.Ordinal))
            .ToList();

        if (patchRows.Count == 0)
        {
            return null;
        }

        var effectivePosition = string.IsNullOrWhiteSpace(position)
            ? ResolveDominantPosition(patchRows)
            : position;

        var scopedRows = string.IsNullOrWhiteSpace(effectivePosition)
            ? patchRows
            : patchRows
                .Where(aggregate => string.Equals(aggregate.Position, effectivePosition, StringComparison.Ordinal))
                .ToList();

        if (scopedRows.Count == 0)
        {
            return null;
        }

        return new ChampionFoundationReadModel
        {
            Summary = BuildSummary(championId, selectedPatchVersion, scopedRows),
            HowToPlay = BuildHowToPlay(scopedRows)
        };
    }

    private static ChampionSummaryReadModel BuildSummary(
        int championId,
        string latestPatchVersion,
        IReadOnlyCollection<ChampionPatternAggregate> rows)
    {
        var totalGames = rows.Sum(row => row.Games);
        var totalWins = rows.Sum(row => row.Wins);
        var trueMainCount = rows
            .Select(row => row.RiotAccountId)
            .Distinct()
            .Count();
        var position = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Position))
            .GroupBy(row => row.Position)
            .Select(group => new
            {
                Position = group.Key,
                Games = group.Sum(row => row.Games)
            })
            .OrderByDescending(group => group.Games)
            .ThenBy(group => group.Position, StringComparer.Ordinal)
            .Select(group => group.Position)
            .FirstOrDefault() ?? string.Empty;

        return new ChampionSummaryReadModel
        {
            ChampionId = championId,
            Games = totalGames,
            WinRate = ComputeRate(totalWins, totalGames),
            TrueMainCount = trueMainCount,
            Position = position,
            LatestPatchVersion = latestPatchVersion,
            LastUpdatedAtUtc = rows.Max(row => row.AggregatedAtUtc)
        };
    }

    private static string ResolveDominantPosition(IReadOnlyCollection<ChampionPatternAggregate> rows)
        => rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Position))
            .GroupBy(row => row.Position)
            .Select(group => new
            {
                Position = group.Key,
                Games = group.Sum(row => row.Games)
            })
            .OrderByDescending(group => group.Games)
            .ThenBy(group => group.Position, StringComparer.Ordinal)
            .Select(group => group.Position)
            .FirstOrDefault() ?? string.Empty;

    private static ChampionHowToPlayFoundationReadModel BuildHowToPlay(IReadOnlyCollection<ChampionPatternAggregate> rows)
    {
        var sampleSize = rows.Sum(row => row.Games);
        var correlatedPatterns = rows
            .GroupBy(BuildCorrelatedPatternKey)
            .Select(group =>
            {
                var first = group.First();
                var summonerPair = NormalizeSummonerPair(first.SummonerSpell1Id, first.SummonerSpell2Id);
                var games = group.Sum(row => row.Games);
                return new CorrelatedPatternReadModel
                {
                    Spell1Id = summonerPair.spell1Id,
                    Spell2Id = summonerPair.spell2Id,
                    SkillOrderSequence = SplitSequence(first.SkillOrderKey),
                    ItemIds = BuildItemSet(first),
                    Games = games,
                    Wins = group.Sum(row => row.Wins)
                };
            })
            .OrderByDescending(pattern => pattern.Games)
            .ThenByDescending(pattern => ComputeRate(pattern.Wins, pattern.Games))
            .ThenBy(pattern => pattern.Spell1Id)
            .ThenBy(pattern => pattern.Spell2Id)
            .ThenBy(pattern => string.Join("-", pattern.SkillOrderSequence), StringComparer.Ordinal)
            .ThenBy(pattern => string.Join("-", pattern.ItemIds), StringComparer.Ordinal)
            .ToList();

        var corePattern = correlatedPatterns.FirstOrDefault(pattern => pattern.ItemIds.Count > 0)
            ?? correlatedPatterns.FirstOrDefault();

        var starterItemOptions = rows
            .Select(row => new
            {
                row.Games,
                row.Wins,
                ItemSet = BuildStarterItemSet(row)
            })
            .Where(entry => entry.ItemSet.Count > 0)
            .GroupBy(entry => string.Join("-", entry.ItemSet))
            .Select(group =>
            {
                var itemSet = group.First().ItemSet;
                var games = group.Sum(entry => entry.Games);
                return new ItemSetOptionReadModel
                {
                    ItemIds = itemSet,
                    Games = games,
                    PlayRate = ComputeRate(games, sampleSize),
                    WinRate = ComputeRate(group.Sum(entry => entry.Wins), games)
                };
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => string.Join("-", option.ItemIds), StringComparer.Ordinal)
            .Take(3)
            .ToList();

        var summonerOptions = rows
            .GroupBy(row => NormalizeSummonerPair(row.SummonerSpell1Id, row.SummonerSpell2Id))
            .Select(group => new SummonerSpellOptionReadModel
            {
                Spell1Id = group.Key.spell1Id,
                Spell2Id = group.Key.spell2Id,
                Games = group.Sum(row => row.Games),
                PlayRate = ComputeRate(group.Sum(row => row.Games), sampleSize),
                WinRate = ComputeRate(group.Sum(row => row.Wins), group.Sum(row => row.Games))
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => option.Spell1Id)
            .ThenBy(option => option.Spell2Id)
            .Take(3)
            .ToList();

        var skillOrderOptions = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.SkillOrderKey))
            .GroupBy(row => row.SkillOrderKey)
            .Select(group => new SkillOrderOptionReadModel
            {
                Sequence = SplitSequence(group.Key),
                Games = group.Sum(row => row.Games),
                PlayRate = ComputeRate(group.Sum(row => row.Games), sampleSize),
                WinRate = ComputeRate(group.Sum(row => row.Wins), group.Sum(row => row.Games))
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => string.Join("-", option.Sequence), StringComparer.Ordinal)
            .Take(3)
            .ToList();

        var itemSetOptions = rows
            .Select(row => new
            {
                row.Games,
                row.Wins,
                ItemSet = BuildItemSet(row)
            })
            .Where(entry => entry.ItemSet.Count > 0)
            .GroupBy(entry => string.Join("-", entry.ItemSet))
            .Select(group =>
            {
                var itemSet = group.First().ItemSet;
                var games = group.Sum(entry => entry.Games);
                return new ItemSetOptionReadModel
                {
                    ItemIds = itemSet,
                    Games = games,
                    PlayRate = ComputeRate(games, sampleSize),
                    WinRate = ComputeRate(group.Sum(entry => entry.Wins), games)
                };
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => string.Join("-", option.ItemIds), StringComparer.Ordinal)
            .Take(3)
            .ToList();

        return new ChampionHowToPlayFoundationReadModel
        {
            SampleSize = sampleSize,
            CoreStarterItems = starterItemOptions.FirstOrDefault(),
            CoreSummonerSpells = corePattern is null
                ? null
                : new SummonerSpellOptionReadModel
                {
                    Spell1Id = corePattern.Spell1Id,
                    Spell2Id = corePattern.Spell2Id,
                    Games = corePattern.Games,
                    PlayRate = ComputeRate(corePattern.Games, sampleSize),
                    WinRate = ComputeRate(corePattern.Wins, corePattern.Games)
                },
            CoreSkillOrder = corePattern is null || corePattern.SkillOrderSequence.Count == 0
                ? null
                : new SkillOrderOptionReadModel
                {
                    Sequence = corePattern.SkillOrderSequence,
                    Games = corePattern.Games,
                    PlayRate = ComputeRate(corePattern.Games, sampleSize),
                    WinRate = ComputeRate(corePattern.Wins, corePattern.Games)
                },
            CoreItemSet = corePattern is null || corePattern.ItemIds.Count == 0
                ? null
                : new ItemSetOptionReadModel
                {
                    ItemIds = corePattern.ItemIds,
                    Games = corePattern.Games,
                    PlayRate = ComputeRate(corePattern.Games, sampleSize),
                    WinRate = ComputeRate(corePattern.Wins, corePattern.Games)
                },
            StarterItemOptions = starterItemOptions,
            SummonerSpellOptions = summonerOptions,
            SkillOrderOptions = skillOrderOptions,
            ItemSetOptions = itemSetOptions
        };
    }

    private static IReadOnlyList<string> SplitSequence(string sequenceKey)
        => string.IsNullOrWhiteSpace(sequenceKey)
            ? []
            : sequenceKey.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<int> BuildItemSet(ChampionPatternAggregate aggregate)
        => new[]
        {
            aggregate.BuildItem0,
            aggregate.BuildItem1,
            aggregate.BuildItem2,
            aggregate.BuildItem3,
            aggregate.BuildItem4,
            aggregate.BuildItem5,
            aggregate.BuildItem6
        }
        .Where(itemId => itemId > 0)
        .ToList();

    private static IReadOnlyList<int> BuildStarterItemSet(ChampionPatternAggregate aggregate)
        => aggregate.StarterItems
            .Where(itemId => itemId > 0)
            .ToList();

    private static string BuildCorrelatedPatternKey(ChampionPatternAggregate aggregate)
    {
        var summonerPair = NormalizeSummonerPair(aggregate.SummonerSpell1Id, aggregate.SummonerSpell2Id);
        var itemSet = string.Join("-", BuildItemSet(aggregate));
        return $"{summonerPair.spell1Id}:{summonerPair.spell2Id}|{aggregate.SkillOrderKey}|{itemSet}";
    }

    private static (int Major, int Minor) ParsePatchVersion(string gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            return (0, 0);
        }

        var segments = gameVersion.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var major = segments.Length > 0 && int.TryParse(segments[0], out var parsedMajor) ? parsedMajor : 0;
        var minor = segments.Length > 1 && int.TryParse(segments[1], out var parsedMinor) ? parsedMinor : 0;
        return (major, minor);
    }

    private static (int spell1Id, int spell2Id) NormalizeSummonerPair(int summoner1Id, int summoner2Id)
    {
        const int FlashId = 4;
        const int SmiteId = 11;

        if (summoner1Id == FlashId || summoner2Id == FlashId)
        {
            return summoner1Id == FlashId
                ? (summoner1Id, summoner2Id)
                : (summoner2Id, summoner1Id);
        }

        if (summoner1Id == SmiteId || summoner2Id == SmiteId)
        {
            return summoner1Id == SmiteId
                ? (summoner1Id, summoner2Id)
                : (summoner2Id, summoner1Id);
        }

        return summoner1Id <= summoner2Id
            ? (summoner1Id, summoner2Id)
            : (summoner2Id, summoner1Id);
    }

    private static double ComputeRate(int numerator, int denominator)
        => denominator == 0 ? 0 : (double)numerator / denominator;

    private sealed class CorrelatedPatternReadModel
    {
        public int Spell1Id { get; init; }

        public int Spell2Id { get; init; }

        public IReadOnlyList<string> SkillOrderSequence { get; init; } = [];

        public IReadOnlyList<int> ItemIds { get; init; } = [];

        public int Games { get; init; }

        public int Wins { get; init; }
    }
}
