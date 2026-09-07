using System.Reflection;
using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.Controllers.Champions;
using TrueMain.Services.Champions.Builds;
using TrueMain.Services.Champions.Composition;
using TrueMain.Services.Champions.Directory;
using TrueMain.Services.Champions.Mains;
using TrueMain.Services.Champions.Matchups;
using TrueMain.Services.Champions.Progression;
using TrueMain.Services.Champions.Scopes;
using TrueMain.Services.Champions.Synergies;

namespace TrueMain.UnitTests;

/// <summary>
/// #1368: every champion read has to go through <see cref="IChampionReadCache"/> — the
/// one place that caches <em>and</em> single-flights, with a sized entry and an
/// aggregation-versioned key. The rule is worth a test because forgetting it is
/// invisible: a service that quietly injects <c>IMemoryCache</c> still returns the
/// right answer, it just re-runs a 14-second scan for every concurrent visitor, and one
/// that injects nothing at all looks tidier than the ones that do.
///
/// <para>The dependency graph asserted here is the constructors of every controller under
/// the <c>/champions</c> prefix: that is the whole DI surface of the champion reads — the set
/// of services the API resolves to answer a <c>/champions</c> request — and it is what a new
/// endpoint has to be added to. Discovered by namespace rather than named one by one, so a
/// controller added to that prefix is covered without anyone remembering to come here
/// (#1520).</para>
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
    public void The_controllers_really_do_expose_the_champion_reads()
    {
        // Guards the guard: if the controllers stopped taking their reads by interface —
        // resolving them from IServiceProvider, say — or if the namespace scan below stopped
        // matching anything, the two theories above would quietly shrink to nothing and pass
        // for ever.
        ChampionQueryServiceImplementations().Should().HaveCountGreaterThan(10);
        ChampionControllers().Should().HaveCountGreaterThan(5);
    }

    private static IReadOnlyList<ParameterInfo> ConstructorParameters(Type implementation)
        => implementation.GetConstructors().Single().GetParameters();

    /// <summary>
    /// Every concrete controller serving the <c>/champions</c> prefix. Found by namespace so
    /// the guard follows the split (#1520) instead of naming one controller that used to hold
    /// all of it.
    /// </summary>
    private static IReadOnlyList<Type> ChampionControllers()
        => typeof(ChampionsControllerBase).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && typeof(ChampionsControllerBase).IsAssignableFrom(type))
            .ToList();

    /// <summary>
    /// The concrete champion query services those controllers depend on, found through their
    /// constructors: every interface under <c>TrueMain.Services.Champions</c> they ask for,
    /// mapped to the single implementation of it in that namespace tree.
    /// </summary>
    private static IReadOnlyList<Type> ChampionQueryServiceImplementations()
    {
        // The prefix, not the exact namespace: the services are grouped into per-feature
        // child namespaces (Builds, Matchups, Composition…), and a read is a champion read
        // wherever under that root it sits.
        var championsRoot = typeof(IChampionReadCache).Namespace![..typeof(IChampionReadCache).Namespace!.LastIndexOf('.')];
        var candidates = typeof(IChampionReadCache).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && IsUnder(type, championsRoot))
            .ToList();

        return ChampionControllers()
            .SelectMany(controller => controller.GetConstructors().Single().GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Where(type => type.IsInterface
                && IsUnder(type, championsRoot)
                && type != typeof(IChampionReadCache))
            .Distinct()
            .Select(contract => candidates.Single(candidate => contract.IsAssignableFrom(candidate)))
            .ToList();
    }

    private static bool IsUnder(Type type, string rootNamespace)
        => type.Namespace is { } ns
           && (ns == rootNamespace || ns.StartsWith(rootNamespace + '.', StringComparison.Ordinal));
}
