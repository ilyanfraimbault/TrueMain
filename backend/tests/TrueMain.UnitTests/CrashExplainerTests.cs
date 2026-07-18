using AwesomeAssertions;
using Data.Logging.Crash;
using TrueMain.Services.Ops;

namespace TrueMain.UnitTests;

/// <summary>
/// The crash explanation (#722) is heuristic display text: these tests pin the
/// source-level readings and the exception-chain refinements so a wording tweak
/// is deliberate, not accidental.
/// </summary>
public sealed class CrashExplainerTests
{
    [Fact]
    public void Explain_UncleanShutdownWithExitCode137_ReadsAsOomKill()
    {
        var explanation = CrashExplainer.Explain(BuildRow(
            source: nameof(CrashSource.UncleanShutdown),
            exitCode: 137));

        explanation.Should().Contain("without a graceful shutdown");
        explanation.Should().Contain("Exit code 137");
        explanation.Should().Contain("out-of-memory");
    }

    [Fact]
    public void Explain_UncleanShutdownWithHugeWorkingSet_ReadsAsProbableOom()
    {
        var explanation = CrashExplainer.Explain(BuildRow(
            source: nameof(CrashSource.UncleanShutdown),
            workingSetBytes: 6L * 1024 * 1024 * 1024));

        explanation.Should().Contain("working set was 6.0 GB");
        explanation.Should().Contain("out-of-memory");
    }

    [Fact]
    public void Explain_UncleanShutdownShortUptime_MentionsCrashLoop()
    {
        var explanation = CrashExplainer.Explain(BuildRow(
            source: nameof(CrashSource.UncleanShutdown),
            uptimeSeconds: 12));

        explanation.Should().Contain("crash loop");
    }

    [Fact]
    public void Explain_HostRun_ReadsAsStartupOrWorkerFailure()
    {
        var explanation = CrashExplainer.Explain(BuildRow(source: nameof(CrashSource.HostRun)));

        explanation.Should().Contain("host's run loop");
        explanation.Should().Contain("startup failure");
    }

    [Fact]
    public void Explain_TaskSchedulerUnobserved_ReadsAsLatentFault()
    {
        var explanation = CrashExplainer.Explain(BuildRow(
            source: nameof(CrashSource.TaskSchedulerUnobserved)));

        explanation.Should().Contain("never awaited or observed");
        explanation.Should().Contain("kept running");
    }

    [Theory]
    [InlineData("Npgsql.NpgsqlException", "PostgreSQL")]
    [InlineData("MongoDB.Driver.MongoConnectionException", "MongoDB")]
    [InlineData("System.Net.Http.HttpRequestException", "network")]
    [InlineData("System.OutOfMemoryException", "memory exhaustion")]
    [InlineData("Microsoft.Extensions.Options.OptionsValidationException", "configuration")]
    public void Explain_RefinesByExceptionType(string exceptionType, string expectedFragment)
    {
        var explanation = CrashExplainer.Explain(BuildRow(
            source: nameof(CrashSource.HostRun),
            exceptionType: exceptionType));

        explanation.Should().Contain(expectedFragment);
    }

    [Fact]
    public void Explain_RefinesByInnerExceptionType_WhenOuterIsGeneric()
    {
        var explanation = CrashExplainer.Explain(BuildRow(
            source: nameof(CrashSource.AppDomainUnhandled),
            exceptionType: "System.AggregateException",
            innerTypes: ["Npgsql.NpgsqlException"]));

        explanation.Should().Contain("An unhandled exception");
        explanation.Should().Contain("PostgreSQL");
    }

    [Fact]
    public void Explain_UnknownSource_FallsBackToGenericText()
    {
        var explanation = CrashExplainer.Explain(BuildRow(source: "SomethingNew"));

        explanation.Should().Be("Recorded process crash.");
    }

    private static CrashRow BuildRow(
        string source,
        string? exceptionType = null,
        IReadOnlyList<string>? innerTypes = null,
        long workingSetBytes = 200L * 1024 * 1024,
        int? exitCode = null,
        double uptimeSeconds = 3600)
        => new(
            Id: "665f000000000000000000aa",
            ReportId: Guid.NewGuid().ToString(),
            TimestampUtc: DateTime.UtcNow,
            ProcessName: "Ingestor",
            Source: source,
            ExceptionType: exceptionType,
            Message: exceptionType is null ? null : "boom",
            StackTrace: null,
            InnerExceptions: (innerTypes ?? [])
                .Select(type => new CrashExceptionInfo(type, "inner boom", null))
                .ToList(),
            Host: "test-host",
            OsDescription: "test-os",
            UptimeSeconds: uptimeSeconds,
            RuntimeVersion: ".NET 10.0",
            AppVersion: "1.0.0",
            WorkingSetBytes: workingSetBytes,
            TotalManagedMemoryBytes: workingSetBytes / 2,
            Gen0Collections: 10,
            Gen1Collections: 5,
            Gen2Collections: 2,
            ExitCode: exitCode,
            RecentLogTail: []);
}
