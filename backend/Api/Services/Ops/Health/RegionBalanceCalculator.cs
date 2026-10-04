using System.Globalization;
using Core.Coverage;
using Data.Configuration;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Health;

/// <summary>
/// What the Ingestor told the admin about its coverage configuration (#1034 channel): the
/// target the deficit is measured against, and the platforms the claim splits across.
/// </summary>
public sealed record IngestorCoverageConfiguration(
    int? TargetMainsPerChampion,
    IReadOnlyList<string>? ClaimPlatforms,
    DateTime? CapturedAtUtc,
    string? UnknownReason);

/// <summary>Everything <see cref="RegionBalanceCalculator.Build"/> reads, already measured.</summary>
public sealed record RegionBalanceInputs
{
    public required DateTime MeasuredAtUtc { get; init; }

    public required int WindowDays { get; init; }

    public required DateTime WindowStartUtc { get; init; }

    public required IngestorCoverageConfiguration Configuration { get; init; }

    public required IReadOnlyDictionary<(string PlatformId, int ChampionId), int> MainsByPlatformChampion { get; init; }

    public required IReadOnlyDictionary<string, int> AccountsByPlatform { get; init; }

    public required IReadOnlyDictionary<string, int> ActiveMainAccountsByPlatform { get; init; }

    public required IReadOnlyList<PlatformDailyMatchesReadModel> DailyMatches { get; init; }
}

/// <summary>
/// The pure half of the region-balance panel (#1153): turns measured counts into the
/// per-platform rows, with the deficit computed by the allocator's own arithmetic.
/// </summary>
public static class RegionBalanceCalculator
{
    public const string IngestorProcessName = "Ingestor";

    /// <summary>
    /// Reads the claim's platforms and the coverage target out of the Ingestor's published
    /// snapshot. Missing values are reported as such, never defaulted: the class default of
    /// the target is not necessarily what the running ingestor uses.
    /// </summary>
    public static IngestorCoverageConfiguration ReadIngestorConfiguration(
        IReadOnlyList<EffectiveConfigurationSnapshot> published)
    {
        ArgumentNullException.ThrowIfNull(published);

        var ingestor = published.FirstOrDefault(snapshot =>
            string.Equals(snapshot.ProcessName, IngestorProcessName, StringComparison.Ordinal));

        if (ingestor is null)
        {
            return new IngestorCoverageConfiguration(
                null, null, null, "The Ingestor has not published its configuration yet.");
        }

        var platformsValue = FindValue(ingestor, "MatchIngestion", "Platforms");
        IReadOnlyList<string>? claimPlatforms = platformsValue?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var targetValue = FindValue(ingestor, "Coverage", "TargetMainsPerChampion");
        int? target = int.TryParse(targetValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                      && parsed > 0
            ? parsed
            : null;

        return new IngestorCoverageConfiguration(
            target,
            claimPlatforms,
            ingestor.CapturedAtUtc,
            target is null
                ? "The Ingestor's published configuration carries no Coverage:TargetMainsPerChampion "
                  + "(it is published from the boot following #1153)."
                : null);
    }

    public static RegionBalanceReadModel Build(RegionBalanceInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var mains = inputs.MainsByPlatformChampion
            .GroupBy(pair => (Normalize(pair.Key.PlatformId), pair.Key.ChampionId))
            .ToDictionary(group => group.Key, group => group.Sum(pair => pair.Value));
        var accounts = SumByPlatform(inputs.AccountsByPlatform);
        var mainAccounts = SumByPlatform(inputs.ActiveMainAccountsByPlatform);
        var daily = inputs.DailyMatches
            .Select(row => row with { PlatformId = Normalize(row.PlatformId) })
            .ToList();
        var matchesByPlatform = daily
            .GroupBy(row => row.PlatformId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Matches), StringComparer.Ordinal);
        var totalMatches = matchesByPlatform.Values.Sum();

        var claimPlatforms = inputs.Configuration.ClaimPlatforms;
        var claimSet = claimPlatforms?.ToHashSet(StringComparer.Ordinal);
        var target = inputs.Configuration.TargetMainsPerChampion;

        // The universe is every champion with an active main anywhere — the snapshot's own
        // denominator, so a champion absent from one region counts as that region's deficit.
        var universe = mains.Keys.Select(key => key.ChampionId).ToHashSet();

        var platformIds = (claimPlatforms ?? [])
            .Concat(accounts.Keys)
            .Concat(mainAccounts.Keys)
            .Concat(matchesByPlatform.Keys)
            .Concat(mains.Keys.Select(key => key.Item1))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(platform => claimSet is null || claimSet.Contains(platform) ? 0 : 1)
            .ThenBy(platform => platform, StringComparer.Ordinal)
            .ToList();

        var deficits = platformIds.ToDictionary(
            platform => platform,
            platform => target is { } t
                ? CoverageDeficit.Mean(universe, championId => MainsFor(mains, platform, championId), t)
                : (double?)null,
            StringComparer.Ordinal);

        var claimWeightTotal = claimPlatforms is null || target is null
            ? 0
            : claimPlatforms.Sum(platform => CoverageDeficit.AllocationWeight(deficits[platform] ?? 0));

        var rows = platformIds.Select(platform =>
        {
            var deficit = deficits[platform];
            int? belowTarget = target is { } t && universe.Count > 0
                ? universe.Count(championId => MainsFor(mains, platform, championId) < t)
                : null;
            var inClaim = claimSet?.Contains(platform);
            var matches = matchesByPlatform.GetValueOrDefault(platform);

            return new PlatformBalanceReadModel
            {
                PlatformId = platform,
                InClaim = inClaim,
                Accounts = accounts.GetValueOrDefault(platform),
                ActiveMainAccounts = mainAccounts.GetValueOrDefault(platform),
                MatchesInWindow = matches,
                MatchShare = totalMatches > 0 ? matches / (double)totalMatches : null,
                MeanCoverageDeficit = deficit,
                ChampionsBelowTarget = belowTarget,
                ChampionsBelowTargetShare = belowTarget is { } below ? below / (double)universe.Count : null,
                ClaimShare = inClaim == true && deficit is { } d && claimWeightTotal > 0
                    ? CoverageDeficit.AllocationWeight(d) / claimWeightTotal
                    : null
            };
        }).ToList();

        return new RegionBalanceReadModel
        {
            MeasuredAtUtc = inputs.MeasuredAtUtc,
            WindowDays = inputs.WindowDays,
            WindowStartUtc = inputs.WindowStartUtc,
            TargetMainsPerChampion = target,
            ClaimPlatforms = claimPlatforms,
            ConfigurationCapturedAtUtc = inputs.Configuration.CapturedAtUtc,
            CoverageUnknownReason = target is null
                ? inputs.Configuration.UnknownReason ?? "The coverage target is unknown."
                : null,
            ChampionUniverse = universe.Count,
            Platforms = rows,
            DailyMatches = daily
                .OrderBy(row => row.Day, StringComparer.Ordinal)
                .ThenBy(row => row.PlatformId, StringComparer.Ordinal)
                .ToList()
        };
    }

    private static string? FindValue(EffectiveConfigurationSnapshot snapshot, string section, string name)
        => snapshot.Sections
            .FirstOrDefault(candidate => string.Equals(candidate.Name, section, StringComparison.Ordinal))?
            .Values
            .FirstOrDefault(value => string.Equals(value.Name, name, StringComparison.Ordinal))?
            .Value;

    private static int MainsFor(
        Dictionary<(string, int), int> mains,
        string platformId,
        int championId)
        => mains.GetValueOrDefault((platformId, championId));

    private static Dictionary<string, int> SumByPlatform(IReadOnlyDictionary<string, int> counts)
        => counts
            .GroupBy(pair => Normalize(pair.Key), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(pair => pair.Value), StringComparer.Ordinal);

    private static string Normalize(string platformId) => platformId.Trim().ToUpperInvariant();
}
