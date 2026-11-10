using AwesomeAssertions;
using Data.Logging.Mongo;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace TrueMain.UnitTests;

public sealed class AuditLogBestEffortTests
{
    [Fact]
    public async Task TryRecordAsync_ReportsAFailedWrite_WithoutThrowing()
    {
        // The change the event records is already committed: a Mongo outage must cost the
        // event, never the request or the run that made the change.
        var auditLog = Substitute.For<IAuditLog>();
        auditLog.RecordAsync(default!, default!, default, default, default, default)
            .ThrowsAsyncForAnyArgs(new TimeoutException("mongo down"));

        var recorded = await auditLog.TryRecordAsync(
            NullLogger.Instance, "seed_account", "operator", "SeedRequest", "id", ct: CancellationToken.None);

        recorded.Should().BeFalse();
    }

    [Fact]
    public async Task TryRecordAsync_PassesTheEventThrough()
    {
        var auditLog = Substitute.For<IAuditLog>();

        var recorded = await auditLog.TryRecordAsync(
            NullLogger.Instance, "seed_account", "operator", "SeedRequest", "id", ct: CancellationToken.None);

        recorded.Should().BeTrue();
        await auditLog.Received(1).RecordAsync("seed_account", "operator", "SeedRequest", "id", null, CancellationToken.None);
    }
}
