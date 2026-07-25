using AwesomeAssertions;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// Issue #259: the worker resolves each process from DI by <see cref="JobMode"/>
/// key instead of by process-name string. The lookup can no longer be broken by a
/// typo, but it can still be broken by a missing registration — so these tests
/// pin that every mode the worker can be configured with actually resolves.
/// </summary>
public sealed class IngestorProcessRegistrationTests
{
    public static TheoryData<JobMode> AllJobModes()
    {
        var data = new TheoryData<JobMode>();
        foreach (var mode in Enum.GetValues<JobMode>())
        {
            data.Add(mode);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllJobModes))]
    public void AddIngestorProcesses_RegistersEveryStep_OfEveryJobMode(JobMode mode)
    {
        var steps = JobModeSequence.For(mode);

        // A mode that expands to nothing would silently do no work for a whole run.
        steps.Should().NotBeEmpty();
        steps.Should().BeSubsetOf(RegisteredProcessKeys());
    }

    [Fact]
    public void AddIngestorProcesses_RegistersExactlyOneProcess_PerSingleProcessMode()
    {
        var keys = RegisteredProcessKeys();

        // Keyed DI silently lets the last registration for a key win, so a
        // duplicate would shadow a process instead of failing like the old
        // name-indexed dictionary did.
        keys.Should().OnlyHaveUniqueItems();

        // Full is a composite expanded by JobModeSequence, so it is the one mode
        // with no process of its own; every other mode must have exactly one.
        keys.Should().BeEquivalentTo(Enum.GetValues<JobMode>().Where(m => m != JobMode.Full));
    }

    [Fact]
    public void AddRecordedProcess_ResolvesTheRecordedWrapper_ByKeyFromAScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Substitute.For<IProcessRunRecorder>());
        services.AddRecordedProcess<StubProcess>(JobMode.DiscoveryOnly);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var otherScope = provider.CreateScope();

        var process = scope.ServiceProvider.GetRequiredKeyedService<IIngestorProcess>(JobMode.DiscoveryOnly);

        // Production registrations must stay wrapped: the worker's per-process
        // catch relies on RecordedProcess having already persisted the Failed run.
        process.Should().BeOfType<RecordedProcess<StubProcess>>();

        // Scoped, not singleton — issue #256 requires the fresh per-process scope
        // to hand out its own instance (and its own DbContext with it).
        otherScope.ServiceProvider.GetRequiredKeyedService<IIngestorProcess>(JobMode.DiscoveryOnly)
            .Should().NotBeSameAs(process);
    }

    [Fact]
    public void For_ReturnsTheModeItself_ForEverySingleProcessMode()
    {
        foreach (var mode in Enum.GetValues<JobMode>().Where(m => m != JobMode.Full))
        {
            JobModeSequence.For(mode).Should().Equal(mode);
        }
    }

    [Fact]
    public void For_Full_RunsEveryProcessOnce()
    {
        var full = JobModeSequence.For(JobMode.Full);

        full.Should().OnlyHaveUniqueItems();
        full.Should().NotContain(JobMode.Full, "Full would expand into itself forever");
    }

    [Fact]
    public void For_Full_HandsBackAnUnmodifiableSequence()
    {
        // For() returns the same shared instance to every caller, so it must not
        // be castable back to something that can reorder the pipeline.
        var full = JobModeSequence.For(JobMode.Full);

        full.Should().NotBeAssignableTo<JobMode[]>();
        ((IList<JobMode>)full).Invoking(list => list[0] = JobMode.MatchDataRetentionOnly)
            .Should().Throw<NotSupportedException>();

        JobModeSequence.For(JobMode.Full).Should().Equal(full);
    }

    [Fact]
    public void For_Throws_ForAnUndefinedMode()
    {
        // The old string mapping fell back to the full pipeline for any unmatched
        // value, so a bogus mode quietly ran everything.
        var act = () => JobModeSequence.For((JobMode)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static List<JobMode> RegisteredProcessKeys()
    {
        var services = new ServiceCollection();
        services.AddIngestorProcesses();

        return services
            .Where(d => d.ServiceType == typeof(IIngestorProcess) && d.IsKeyedService)
            .Select(d => d.ServiceKey)
            .OfType<JobMode>()
            .ToList();
    }

    private sealed class StubProcess : IIngestorProcess
    {
        public string Name => "Stub";

        public Task<object?> RunCoreAsync(CancellationToken ct) => Task.FromResult<object?>(null);
    }
}
