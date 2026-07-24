using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

public sealed class PatchSortKeyResolverTests
{
    private const string Surface = "champions-trend";

    [Fact]
    public void Resolve_returns_major_minor_and_stays_silent_on_a_valid_patch()
    {
        var logger = new CapturingLogger();
        var resolver = new PatchSortKeyResolver(logger, Surface, championId: 157);

        resolver.Resolve("16.4.521.9999").Should().Be((16, 4), "the hotfix build never enters the sort key");
        resolver.Resolve("16.10").Should().Be((16, 10));

        logger.Entries.Should().BeEmpty("well-formed rows are not worth an ops log line");
    }

    [Fact]
    public void Resolve_warns_once_for_a_malformed_value_repeated_across_rows()
    {
        var logger = new CapturingLogger();
        var resolver = new PatchSortKeyResolver(logger, Surface, championId: 157);

        // The same corrupt row value re-read 50 times — once per row of the
        // result set, plus a second ordering pass over the same rows.
        for (var i = 0; i < 50; i++)
        {
            resolver.Resolve("not-a-patch").Should().Be((0, 0), "the fallback sort key is unchanged");
        }

        var entry = logger.Entries.Should().ContainSingle(
            "a value repeated across rows is one data-quality signal, not fifty").Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Properties.Should().Contain(new KeyValuePair<string, object?>("GameVersion", "not-a-patch"));
        entry.Properties.Should().Contain(new KeyValuePair<string, object?>("ChampionId", 157));
        entry.Properties.Should().Contain(new KeyValuePair<string, object?>("Surface", Surface));
        entry.Message.Should().Contain("not-a-patch", "the offending value must be readable in the log line");
    }

    [Fact]
    public void Resolve_warns_once_per_distinct_malformed_value()
    {
        var logger = new CapturingLogger();
        var resolver = new PatchSortKeyResolver(logger, Surface, championId: 157);

        string[] versions = ["", "16", "not-a-patch", "16", "16.4", "not-a-patch", "  "];
        foreach (var version in versions)
        {
            _ = resolver.Resolve(version);
        }

        var reported = logger.Entries
            .Select(logEntry => logEntry.Properties.Single(pair => pair.Key == "GameVersion").Value as string)
            .ToList();
        string?[] expected = ["", "16", "not-a-patch", "  "];

        reported.Should().BeEquivalentTo(expected,
            "each distinct offender is reported exactly once; the valid patch is not reported");
    }

    [Fact]
    public void Resolve_scopes_the_warned_set_to_one_instance()
    {
        var logger = new CapturingLogger();

        // One resolver per query: the same corrupt row seen by a later query is
        // a fresh signal, so it warns again instead of being silenced forever.
        _ = new PatchSortKeyResolver(logger, Surface, championId: 157).Resolve("not-a-patch");
        _ = new PatchSortKeyResolver(logger, Surface, championId: 157).Resolve("not-a-patch");

        logger.Entries.Should().HaveCount(2);
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyList<KeyValuePair<string, object?>> Properties);

    private sealed class CapturingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(
                logLevel,
                formatter(state, exception),
                state as IReadOnlyList<KeyValuePair<string, object?>> ?? []));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
