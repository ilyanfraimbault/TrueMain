using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Time.Testing;
using TrueMain.ReadModels.Ops;
using TrueMain.Services;
using TrueMain.Services.Ops;
using TrueMain.Services.Ops.Navigation;

namespace TrueMain.UnitTests;

/// <summary>
/// The admin sidebar's health dot (#2294) reads the warm cockpit payload (#2305) without being a
/// reader of it, never computes one, and drops a verdict too old to stand for now.
/// </summary>
public sealed class NavSummaryHealthTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 26, 8, 0, 0, DateTimeKind.Utc);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 1024 });
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(Now));

    public void Dispose() => _cache.Dispose();

    [Fact]
    public void PeekValue_IsNull_WhenNothingWasComputed()
    {
        var payload = NewPayload();

        payload.PeekValue(_cache).Should().BeNull();
        payload.Peek(_cache).Should().BeNull();
    }

    [Fact]
    public async Task PeekValue_ReturnsTheHeldValue_WithoutMarkingItRead()
    {
        var payload = NewPayload();
        var held = await payload.GetAsync(_cache, () => Task.FromResult(Health("red", Now)), _clock, CancellationToken.None, ownerAwaitsToCompletion: true);
        var readAt = payload.Peek(_cache)!.LastReadAtUtc;

        _clock.Advance(TimeSpan.FromMinutes(5));
        payload.PeekValue(_cache).Should().BeSameAs(held);

        payload.Peek(_cache)!.LastReadAtUtc.Should().Be(readAt);
    }

    [Fact]
    public void CurrentHealth_IsNull_WithoutAHeldEvaluation()
        => NavSummaryQueryService.CurrentHealth(null, Now).Should().BeNull();

    [Fact]
    public void CurrentHealth_KeepsAnEvaluationWithinTheRefresherIdleWindow()
    {
        var held = Health("amber", Now - CockpitCacheRefresher.IdleWindow);

        NavSummaryQueryService.CurrentHealth(held, Now).Should().BeSameAs(held);
    }

    [Fact]
    public void CurrentHealth_DropsAnEvaluationOlderThanTheIdleWindow()
    {
        var held = Health("red", Now - CockpitCacheRefresher.IdleWindow - TimeSpan.FromSeconds(1));

        NavSummaryQueryService.CurrentHealth(held, Now).Should().BeNull();
    }

    private static WarmPayload<PipelineHealthReadModel> NewPayload()
        => new($"test:nav-health:{Guid.NewGuid():N}", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

    private static PipelineHealthReadModel Health(string status, DateTime evaluatedAtUtc)
        => new() { Status = status, Headline = status, EvaluatedAtUtc = evaluatedAtUtc };
}
