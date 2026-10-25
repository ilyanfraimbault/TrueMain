using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Personas;

namespace TrueMain.UnitTests;

/// <summary>
/// The same-name conflict sweep the API runs once at startup (#2273): it sweeps through a scope of
/// its own, and a database failure is logged rather than taking the host down.
/// </summary>
public sealed class PersonaAutoMergeStartupSweepTests
{
    [Fact]
    public async Task Start_SweepsTheSameNameConflictsOnce()
    {
        var sweep = Substitute.For<IPersonaAutoMergeService>();
        sweep.MergeHomonymConflictsAsync(Arg.Any<CancellationToken>()).Returns(new PersonaAutoMergeResponse { Merged = 3 });

        await RunAsync(sweep);

        await sweep.Received(1).MergeHomonymConflictsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Start_SurvivesAFailedSweep()
    {
        var sweep = Substitute.For<IPersonaAutoMergeService>();
        sweep.MergeHomonymConflictsAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("database down"));

        var run = () => RunAsync(sweep);

        await run.Should().NotThrowAsync();
    }

    private static async Task RunAsync(IPersonaAutoMergeService sweep)
    {
        await using var provider = new ServiceCollection().AddSingleton(sweep).BuildServiceProvider();
        using var service = new PersonaAutoMergeStartupSweep(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PersonaAutoMergeStartupSweep>.Instance,
            TimeSpan.Zero);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;
        await service.StopAsync(CancellationToken.None);
    }
}
