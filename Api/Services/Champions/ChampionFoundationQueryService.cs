using Core.Options;
using TrueMain.ReadModels.Champions;
using Data;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Champions;

public sealed class ChampionFoundationQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options) : IChampionFoundationQueryService
{
    public async Task<ChampionFoundationReadModel?> GetAsync(int championId, CancellationToken ct)
    {
        var specialistMatches = from participant in db.MatchParticipants.AsNoTracking()
            join match in db.Matches.AsNoTracking() on participant.MatchId equals match.Id
            join stat in db.MainChampionStats.AsNoTracking()
                on new { match.PlatformId, participant.Puuid, participant.ChampionId }
                equals new { stat.PlatformId, stat.Puuid, stat.ChampionId }
            where participant.ChampionId == championId
                && stat.IsMain
                && match.QueueId == options.Value.QueueId
            select new
            {
                match.GameVersion,
                match.GameStartTimeUtc,
                BaseSample = new SpecialistParticipantBaseSample
                {
                    ParticipantRecordId = participant.Id,
                    PlatformId = match.PlatformId,
                    Puuid = participant.Puuid,
                    Win = participant.Win,
                    Summoner1Id = participant.Summoner1Id,
                    Summoner2Id = participant.Summoner2Id,
                    Item0 = participant.Item0,
                    Item1 = participant.Item1,
                    Item2 = participant.Item2,
                    Item3 = participant.Item3,
                    Item4 = participant.Item4,
                    Item5 = participant.Item5,
                    GameVersion = match.GameVersion,
                    GameStartTimeUtc = match.GameStartTimeUtc,
                    PrimaryPosition = stat.PrimaryPosition,
                    IsOtp = stat.IsOtp,
                    CalculatedAtUtc = stat.CalculatedAtUtc
                }
            };

        var latestGameVersion = await specialistMatches
            .OrderByDescending(entry => entry.GameStartTimeUtc)
            .Select(entry => entry.GameVersion)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(latestGameVersion))
        {
            return null;
        }

        var latestPatchVersion = NormalizePatchVersion(latestGameVersion);
        var latestPatchPattern = $"{latestPatchVersion}.%";

        var baseSamples = await specialistMatches
            .Where(entry => entry.GameVersion == latestPatchVersion
                || entry.GameVersion.StartsWith(latestPatchPattern))
            .Select(entry => entry.BaseSample)
            .ToListAsync(ct);

        if (baseSamples.Count == 0)
        {
            return null;
        }

        var participantIds = baseSamples.Select(sample => sample.ParticipantRecordId).ToArray();
        var skillSequences = await db.Database
            .SqlQuery<ParticipantSkillSequenceProjection>($"""
                SELECT
                    mp."Id" AS "ParticipantRecordId",
                    COALESCE(
                        (
                            SELECT string_agg(
                                CASE (entry.elem->>'SkillSlot')::int
                                    WHEN 1 THEN 'Q'
                                    WHEN 2 THEN 'W'
                                    WHEN 3 THEN 'E'
                                    WHEN 4 THEN 'R'
                                    ELSE entry.elem->>'SkillSlot'
                                END,
                                '-' ORDER BY (entry.elem->>'TimestampMs')::int)
                            FROM (
                                SELECT elem
                                FROM jsonb_array_elements(to_jsonb(mp."SkillEvents")) AS elem
                                WHERE elem->>'LevelUpType' ILIKE 'NORMAL'
                                ORDER BY (elem->>'TimestampMs')::int
                                LIMIT 3
                            ) AS entry
                        ),
                        ''
                    ) AS "SequenceKey"
                FROM match_participants AS mp
                WHERE mp."Id" = ANY ({participantIds})
                """)
            .ToDictionaryAsync(
                entry => entry.ParticipantRecordId,
                entry => string.IsNullOrWhiteSpace(entry.SequenceKey)
                    ? Array.Empty<string>()
                    : entry.SequenceKey.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                ct);

        var samples = baseSamples.Select(sample => new SpecialistParticipantSample
        {
            PlatformId = sample.PlatformId,
            Puuid = sample.Puuid,
            Win = sample.Win,
            Summoner1Id = sample.Summoner1Id,
            Summoner2Id = sample.Summoner2Id,
            SkillSequence = skillSequences.GetValueOrDefault(sample.ParticipantRecordId, Array.Empty<string>()),
            Item0 = sample.Item0,
            Item1 = sample.Item1,
            Item2 = sample.Item2,
            Item3 = sample.Item3,
            Item4 = sample.Item4,
            Item5 = sample.Item5,
            GameVersion = sample.GameVersion,
            GameStartTimeUtc = sample.GameStartTimeUtc,
            PrimaryPosition = sample.PrimaryPosition,
            IsOtp = sample.IsOtp,
            CalculatedAtUtc = sample.CalculatedAtUtc
        }).ToList();

        var summary = BuildSummary(championId, samples);
        var howToPlay = BuildHowToPlay(samples);

        return new ChampionFoundationReadModel
        {
            Summary = summary,
            HowToPlay = howToPlay
        };
    }

    private static ChampionSummaryReadModel BuildSummary(int championId, IReadOnlyCollection<SpecialistParticipantSample> samples)
    {
        var totalGames = samples.Count;
        var specialistCount = samples
            .Select(sample => (sample.PlatformId, sample.Puuid))
            .Distinct()
            .Count();
        var otpCount = samples
            .Where(sample => sample.IsOtp)
            .Select(sample => (sample.PlatformId, sample.Puuid))
            .Distinct()
            .Count();
        var primaryPosition = samples
            .Where(sample => !string.IsNullOrWhiteSpace(sample.PrimaryPosition))
            .GroupBy(sample => sample.PrimaryPosition)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => group.Key)
            .FirstOrDefault() ?? string.Empty;
        var latestPatchVersion = samples
            .OrderByDescending(sample => sample.GameStartTimeUtc)
            .Select(sample => NormalizePatchVersion(sample.GameVersion))
            .FirstOrDefault() ?? string.Empty;

        return new ChampionSummaryReadModel
        {
            ChampionId = championId,
            Games = totalGames,
            WinRate = ComputeRate(samples.Count(sample => sample.Win), totalGames),
            SpecialistCount = specialistCount,
            OtpCount = otpCount,
            PrimaryPosition = primaryPosition,
            LatestPatchVersion = latestPatchVersion,
            LastUpdatedAtUtc = samples.Max(sample => sample.CalculatedAtUtc)
        };
    }

    private static ChampionHowToPlayFoundationReadModel BuildHowToPlay(IReadOnlyCollection<SpecialistParticipantSample> samples)
    {
        var sampleSize = samples.Count;

        var summonerOptions = samples
            .GroupBy(sample => NormalizeSummonerPair(sample.Summoner1Id, sample.Summoner2Id))
            .Select(group => new SummonerSpellOptionReadModel
            {
                Spell1Id = group.Key.spell1Id,
                Spell2Id = group.Key.spell2Id,
                Games = group.Count(),
                PlayRate = ComputeRate(group.Count(), sampleSize),
                WinRate = ComputeRate(group.Count(sample => sample.Win), group.Count())
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => option.Spell1Id)
            .ThenBy(option => option.Spell2Id)
            .Take(3)
            .ToList();

        var skillOrderOptions = samples
            .Select(sample => new
            {
                sample.Win,
                Sequence = sample.SkillSequence
            })
            .Where(entry => entry.Sequence.Count > 0)
            .GroupBy(entry => string.Join("-", entry.Sequence))
            .Select(group =>
            {
                var sequence = group.First().Sequence;
                return new SkillOrderOptionReadModel
                {
                    Sequence = sequence,
                    Games = group.Count(),
                    PlayRate = ComputeRate(group.Count(), sampleSize),
                    WinRate = ComputeRate(group.Count(entry => entry.Win), group.Count())
                };
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => string.Join("-", option.Sequence))
            .Take(3)
            .ToList();

        var itemSetOptions = samples
            .Select(sample => new
            {
                sample.Win,
                ItemSet = BuildItemSet(sample)
            })
            .Where(entry => entry.ItemSet.Count > 0)
            .GroupBy(entry => string.Join("-", entry.ItemSet))
            .Select(group =>
            {
                var itemSet = group.First().ItemSet;
                return new ItemSetOptionReadModel
                {
                    ItemIds = itemSet,
                    Games = group.Count(),
                    PlayRate = ComputeRate(group.Count(), sampleSize),
                    WinRate = ComputeRate(group.Count(entry => entry.Win), group.Count())
                };
            })
            .OrderByDescending(option => option.Games)
            .ThenByDescending(option => option.WinRate)
            .ThenBy(option => string.Join("-", option.ItemIds))
            .Take(3)
            .ToList();

        return new ChampionHowToPlayFoundationReadModel
        {
            SampleSize = sampleSize,
            CoreSummonerSpells = summonerOptions.FirstOrDefault(),
            CoreSkillOrder = skillOrderOptions.FirstOrDefault(),
            CoreItemSet = itemSetOptions.FirstOrDefault(),
            SummonerSpellOptions = summonerOptions,
            SkillOrderOptions = skillOrderOptions,
            ItemSetOptions = itemSetOptions
        };
    }

    private static double ComputeRate(int numerator, int denominator)
        => denominator == 0 ? 0 : (double)numerator / denominator;

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

    private static (int spell1Id, int spell2Id) NormalizeSummonerPair(int summoner1Id, int summoner2Id)
        => summoner1Id <= summoner2Id
            ? (summoner1Id, summoner2Id)
            : (summoner2Id, summoner1Id);

    private static IReadOnlyList<int> BuildItemSet(SpecialistParticipantSample sample)
        => new[] { sample.Item0, sample.Item1, sample.Item2, sample.Item3, sample.Item4, sample.Item5 }
            .Where(itemId => itemId > 0)
            .ToList();

    private sealed class SpecialistParticipantBaseSample
    {
        public Guid ParticipantRecordId { get; init; }

        public string PlatformId { get; init; } = string.Empty;

        public string Puuid { get; init; } = string.Empty;

        public bool Win { get; init; }

        public int Summoner1Id { get; init; }

        public int Summoner2Id { get; init; }

        public int Item0 { get; init; }

        public int Item1 { get; init; }

        public int Item2 { get; init; }

        public int Item3 { get; init; }

        public int Item4 { get; init; }

        public int Item5 { get; init; }

        public string GameVersion { get; init; } = string.Empty;

        public DateTime GameStartTimeUtc { get; init; }

        public string PrimaryPosition { get; init; } = string.Empty;

        public bool IsOtp { get; init; }

        public DateTime CalculatedAtUtc { get; init; }
    }

    private sealed class SpecialistParticipantSample
    {
        public string PlatformId { get; init; } = string.Empty;

        public string Puuid { get; init; } = string.Empty;

        public bool Win { get; init; }

        public int Summoner1Id { get; init; }

        public int Summoner2Id { get; init; }

        public IReadOnlyList<string> SkillSequence { get; init; } = [];

        public int Item0 { get; init; }

        public int Item1 { get; init; }

        public int Item2 { get; init; }

        public int Item3 { get; init; }

        public int Item4 { get; init; }

        public int Item5 { get; init; }

        public string GameVersion { get; init; } = string.Empty;

        public DateTime GameStartTimeUtc { get; init; }

        public string PrimaryPosition { get; init; } = string.Empty;

        public bool IsOtp { get; init; }

        public DateTime CalculatedAtUtc { get; init; }
    }

    private sealed class ParticipantSkillSequenceProjection
    {
        public Guid ParticipantRecordId { get; set; }

        public string SequenceKey { get; set; } = string.Empty;
    }
}
