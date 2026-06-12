using AwesomeAssertions;
using Ingestor.Services;

namespace TrueMain.UnitTests;

public sealed class IterationContextTests
{
    [Fact]
    public void CurrentIterationId_IsNull_OutsideAnyIteration()
    {
        var context = new IterationContext();

        context.CurrentIterationId.Should().BeNull();
    }

    [Fact]
    public void BeginIteration_SetsTheCurrentId_UntilTheScopeIsDisposed()
    {
        var context = new IterationContext();

        Guid scopedId;
        using (var scope = context.BeginIteration())
        {
            scopedId = scope.IterationId;
            scopedId.Should().NotBe(Guid.Empty);
            context.CurrentIterationId.Should().Be(scopedId);
        }

        // The scope restored the prior (null) value when disposed.
        context.CurrentIterationId.Should().BeNull();
    }

    [Fact]
    public void BeginIteration_MintsAFreshId_PerPass()
    {
        var context = new IterationContext();

        Guid first;
        using (var scope = context.BeginIteration())
        {
            first = scope.IterationId;
        }

        using var second = context.BeginIteration();
        second.IterationId.Should().NotBe(first);
    }

    [Fact]
    public async Task CurrentIterationId_FlowsIntoAwaitedWork_WithinTheScope()
    {
        var context = new IterationContext();

        using var scope = context.BeginIteration();

        // The AsyncLocal value must be observable inside awaited continuations —
        // that is exactly how the recorder reads it from a process's RunCoreAsync.
        async Task<Guid?> ReadAfterAwaitAsync()
        {
            await Task.Yield();
            return context.CurrentIterationId;
        }

        var observed = await ReadAfterAwaitAsync();
        observed.Should().Be(scope.IterationId);
    }
}
