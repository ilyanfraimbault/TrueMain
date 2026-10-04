using AwesomeAssertions;
using Data.Configuration;
using Ingestor.Processes.Components.Coverage;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Health;

namespace TrueMain.UnitTests;

/// <summary>
/// The region-balance panel (#1153) is only worth reading if the deficit it shows is the one
/// the claim allocator acted on, so the central test here computes both from the same counts
/// and requires them to agree. The rest pins what would make the panel lie: a guessed target,
/// a narrowed claim scope hidden, or a zero where nothing was measured.
/// </summary>
public sealed class RegionBalanceCalculatorTests
{
    private static readonly DateTime WindowStart = new(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);

    // EUW1 is fully covered, KR holds half the target on one champion and none on the
    // other, NA1 holds nothing at all: the 82/14/4 shape in miniature.
    private static readonly Dictionary<(string PlatformId, int ChampionId), int> Mains = new()
    {
        [("EUW1", 1)] = 60,
        [("EUW1", 2)] = 50,
        [("KR", 1)] = 25
    };

    [Fact]
    public void DeficitAndClaimShareMatchWhatTheAllocatorUses()
    {
        var model = RegionBalanceCalculator.Build(Inputs(target: 50, claim: ["EUW1", "KR", "NA1"]));
        var snapshot = new ChampionCoverageSnapshot(Mains, 50);

        foreach (var row in model.Platforms)
        {
            row.MeanCoverageDeficit.Should().BeApproximately(snapshot.MeanDeficit(row.PlatformId), 1e-12);
        }

        // A large batch rounds to the exact shares, so the allocator's own split is the oracle.
        const int batch = 100_000;
        var quotas = PlatformBudgetAllocator.Allocate(["EUW1", "KR", "NA1"], batch, snapshot);
        foreach (var row in model.Platforms)
        {
            row.ClaimShare!.Value.Should().BeApproximately(quotas[row.PlatformId] / (double)batch, 1e-4);
        }

        model.Platforms.Sum(row => row.ClaimShare!.Value).Should().BeApproximately(1, 1e-12);
    }

    [Fact]
    public void CountsChampionsBelowTargetOverTheSharedUniverse()
    {
        var model = RegionBalanceCalculator.Build(Inputs(target: 50, claim: ["EUW1", "KR", "NA1"]));

        model.ChampionUniverse.Should().Be(2);
        var byPlatform = model.Platforms.ToDictionary(row => row.PlatformId);
        byPlatform["EUW1"].ChampionsBelowTarget.Should().Be(0);
        byPlatform["KR"].ChampionsBelowTarget.Should().Be(2);
        byPlatform["KR"].MeanCoverageDeficit.Should().BeApproximately(0.75, 1e-12);
        byPlatform["NA1"].ChampionsBelowTargetShare.Should().Be(1);
        byPlatform["NA1"].MeanCoverageDeficit.Should().Be(1);
    }

    [Fact]
    public void UnknownTargetLeavesEveryCoverageFigureNullWithAReason()
    {
        var model = RegionBalanceCalculator.Build(Inputs(target: null, claim: ["EUW1", "KR"]));

        model.CoverageUnknownReason.Should().NotBeNullOrWhiteSpace();
        model.Platforms.Should().OnlyContain(row =>
            row.MeanCoverageDeficit == null && row.ChampionsBelowTarget == null && row.ClaimShare == null);
        // The counts that do not depend on the target are still there.
        model.Platforms.Single(row => row.PlatformId == "EUW1").Accounts.Should().Be(800);
    }

    [Fact]
    public void APlatformOutsideTheClaimIsShownAndFlaggedNotDropped()
    {
        var model = RegionBalanceCalculator.Build(Inputs(target: 50, claim: ["EUW1", "KR"]));

        var na = model.Platforms.Single(row => row.PlatformId == "NA1");
        na.InClaim.Should().BeFalse();
        na.ClaimShare.Should().BeNull();
        model.Platforms[^1].PlatformId.Should().Be("NA1", "claim platforms are listed first");
        model.Platforms.Where(row => row.InClaim == true).Sum(row => row.ClaimShare!.Value)
            .Should().BeApproximately(1, 1e-12);
    }

    [Fact]
    public void MatchSharesComeFromTheWindowAndAreNullWhenNothingWasIngested()
    {
        var model = RegionBalanceCalculator.Build(Inputs(target: 50, claim: ["EUW1", "KR", "NA1"]));
        var byPlatform = model.Platforms.ToDictionary(row => row.PlatformId);

        byPlatform["EUW1"].MatchesInWindow.Should().Be(90);
        byPlatform["EUW1"].MatchShare.Should().BeApproximately(0.9, 1e-12);
        byPlatform["NA1"].MatchesInWindow.Should().Be(0);

        var empty = RegionBalanceCalculator.Build(Inputs(target: 50, claim: ["EUW1"]) with { DailyMatches = [] });
        empty.Platforms.Should().OnlyContain(row => row.MatchShare == null);
    }

    [Fact]
    public void ReadsTheClaimPlatformsAndTargetFromTheIngestorSnapshot()
    {
        var snapshot = new EffectiveConfigurationSnapshot
        {
            ProcessName = "Ingestor",
            CapturedAtUtc = WindowStart,
            Sections =
            [
                Section("MatchIngestion", "Platforms", "KR, euw1, NA1"),
                Section("Coverage", "TargetMainsPerChampion", "50")
            ]
        };

        var configuration = RegionBalanceCalculator.ReadIngestorConfiguration([snapshot]);

        configuration.TargetMainsPerChampion.Should().Be(50);
        configuration.ClaimPlatforms.Should().Equal("KR", "EUW1", "NA1");
        configuration.UnknownReason.Should().BeNull();
    }

    [Fact]
    public void ASnapshotWithoutTheCoverageSectionReportsTheTargetUnknown()
    {
        var snapshot = new EffectiveConfigurationSnapshot
        {
            ProcessName = "Ingestor",
            Sections = [Section("MatchIngestion", "Platforms", "KR")]
        };

        var configuration = RegionBalanceCalculator.ReadIngestorConfiguration([snapshot]);

        configuration.TargetMainsPerChampion.Should().BeNull();
        configuration.ClaimPlatforms.Should().Equal("KR");
        configuration.UnknownReason.Should().NotBeNullOrWhiteSpace();

        RegionBalanceCalculator.ReadIngestorConfiguration([]).ClaimPlatforms.Should().BeNull();
    }

    private static RegionBalanceInputs Inputs(int? target, IReadOnlyList<string> claim) => new()
    {
        WindowDays = 14,
        WindowStartUtc = WindowStart,
        Configuration = new IngestorCoverageConfiguration(target, claim, WindowStart, target is null ? "missing" : null),
        MainsByPlatformChampion = Mains,
        AccountsByPlatform = new Dictionary<string, int> { ["EUW1"] = 800, ["KR"] = 150, ["NA1"] = 40 },
        ActiveMainAccountsByPlatform = new Dictionary<string, int> { ["EUW1"] = 100, ["KR"] = 20 },
        DailyMatches =
        [
            new PlatformDailyMatchesReadModel { Day = "2026-09-21", PlatformId = "EUW1", Matches = 50 },
            new PlatformDailyMatchesReadModel { Day = "2026-09-22", PlatformId = "EUW1", Matches = 40 },
            new PlatformDailyMatchesReadModel { Day = "2026-09-22", PlatformId = "KR", Matches = 10 }
        ]
    };

    private static EffectiveConfigurationSection Section(string name, string valueName, string value) => new()
    {
        Name = name,
        Values = [new EffectiveConfigurationValue { Key = name + ":" + valueName, Name = valueName, Value = value }]
    };
}
