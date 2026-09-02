using System.Reflection;
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.Controllers.Champions;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

/// <summary>
/// #1368: every champion read has to go through <see cref="IChampionReadCache"/> — the
/// one place that caches <em>and</em> single-flights, with a sized entry and an
/// aggregation-versioned key. The rule is worth a test because forgetting it is
/// invisible: a service that quietly injects <c>IMemoryCache</c> still returns the
/// right answer, it just re-runs a 14-second scan for every concurrent visitor, and one
/// that injects nothing at all looks tidier than the ones that do.
///
/// <para>The dependency graph asserted here is the champion controller's constructor:
/// that is the whole DI surface of the champion reads — the set of services the API
/// resolves to answer a <c>/champions</c> request — and it is what a new endpoint has
/// to be added to.</para>
/// </summary>
public sealed class ChampionReadCacheRegistrationTests
{
    public static TheoryData<Type> ChampionQueryServices()
    {
        var data = new TheoryData<Type>();
        foreach (var implementation in ChampionQueryServiceImplementations())
        {
            data.Add(implementation);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ChampionQueryServices))]
    public void Every_champion_query_service_takes_the_shared_read_cache(Type implementation)
    {
        ConstructorParameters(implementation)
            .Should().Contain(parameter => parameter.ParameterType == typeof(IChampionReadCache),
                "{0} answers a champion request, so its result must be cached and coalesced " +
                "through the one entry point rather than recomputed per caller",
                implementation.Name);
    }

    [Theory]
    [MemberData(nameof(ChampionQueryServices))]
    public void No_champion_query_service_caches_on_its_own(Type implementation)
    {
        ConstructorParameters(implementation)
            .Should().NotContain(parameter => parameter.ParameterType == typeof(IMemoryCache),
                "{0} would then be caching without single-flighting, which is how one " +
                "expiry became ten identical scans of the same slice",
                implementation.Name);
    }

    [Fact]
    public void The_controller_really_does_expose_the_champion_reads()
    {
        // Guards the guard: if the controller stopped taking its reads by interface —
        // resolving them from IServiceProvider, say — the two theories above would
        // quietly shrink to nothing and pass for ever.
        ChampionQueryServiceImplementations().Should().HaveCountGreaterThan(10);
    }

    private static IReadOnlyList<ParameterInfo> ConstructorParameters(Type implementation)
        => implementation.GetConstructors().Single().GetParameters();

    /// <summary>
    /// The concrete champion query services the champion controller depends on, found
    /// through its constructor: every <c>TrueMain.Services.Champions</c> interface it
    /// asks for, mapped to the single implementation of it in that namespace.
    /// </summary>
    private static IReadOnlyList<Type> ChampionQueryServiceImplementations()
    {
        var championsNamespace = typeof(IChampionReadCache).Namespace!;
        var candidates = typeof(IChampionReadCache).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.Namespace == championsNamespace)
            .ToList();

        return typeof(ChampionsController)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Where(type => type.IsInterface
                && type.Namespace == championsNamespace
                && type != typeof(IChampionReadCache))
            .Distinct()
            .Select(contract => candidates.Single(candidate => contract.IsAssignableFrom(candidate)))
            .ToList();
    }
}
