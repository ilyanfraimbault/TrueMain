using AwesomeAssertions;
using Data.Ops.Mongo;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Alerting;

namespace TrueMain.UnitTests;

/// <summary>
/// The health alerter's rules (#2292): transitions only, a red held for two evaluations, a
/// cooldown per key, nothing on the observing pass after boot, and stale streaks forgotten.
/// </summary>
public sealed class HealthAlertTransitionsTests
{
    private static readonly DateTime Start = new(2026, 10, 26, 8, 0, 0, DateTimeKind.Utc);
    private static readonly AlertingOptions Options = new()
    {
        DiscordWebhookUrl = "https://discord.test/api/webhooks/1/secret",
        EvaluationInterval = TimeSpan.FromMinutes(5),
        RedConfirmations = 2,
        Cooldown = TimeSpan.FromHours(1),
        ProcessFailureThreshold = 3
    };

    [Fact]
    public void ARed_MustHoldForTwoEvaluations_AndIsAnnouncedOnce()
    {
        var run = new Run();

        run.Next(Health("red")).Should().BeEmpty("one red evaluation is not an incident yet");
        var opened = run.Next(Health("red"));
        run.Next(Health("red")).Should().BeEmpty("a held red is not a transition");

        var change = opened.Should().ContainSingle().Subject;
        change.Key.Should().Be("dataQuality");
        change.Kind.Should().Be(HealthAlertChangeKind.Failing);
        change.Headline.Should().Be("dataQuality is red");
        run.State!.Tracked.Single(entry => entry.Key == "dataQuality").Open.Should().BeTrue();
    }

    [Fact]
    public void ARedThatClearsBeforeItIsConfirmed_SendsNothing()
    {
        var run = new Run();

        run.Next(Health("red")).Should().BeEmpty();
        run.Next(Health("green")).Should().BeEmpty();
        run.Next(Health("red")).Should().BeEmpty("the streak restarted");
    }

    [Fact]
    public void AnAnnouncedIncident_IsAnnouncedAgainWhenItRecovers_AndOnlyThen()
    {
        var run = new Run();
        run.Next(Health("red"));
        run.Next(Health("red"));

        var recovered = run.Next(Health("amber"));
        run.Next(Health("green")).Should().BeEmpty();

        recovered.Should().ContainSingle().Which.Kind.Should().Be(HealthAlertChangeKind.Recovered);
        run.State!.Tracked.Single(entry => entry.Key == "dataQuality").Open.Should().BeFalse();
    }

    [Fact]
    public void AnUnknownSignal_NeitherOpensNorClosesAnIncident()
    {
        var run = new Run();
        run.Next(Health("red"));
        run.Next(Health("unknown")).Should().BeEmpty();
        run.Next(Health("red")).Should().ContainSingle("the unknown sample kept the streak");

        run.Next(Health("unknown")).Should().BeEmpty("not measured is not recovered");
        run.Next(Health("green")).Should().ContainSingle().Which.Kind.Should().Be(HealthAlertChangeKind.Recovered);
    }

    [Fact]
    public void AFlappingSignal_IsAnnouncedOncePerCooldown_ThenAgainIfStillFailing()
    {
        var run = new Run();
        run.Next(Health("red"));
        run.Next(Health("red")).Should().ContainSingle();
        run.Next(Health("green")).Should().ContainSingle("the announced incident recovered");

        run.Next(Health("red")).Should().BeEmpty();
        run.Next(Health("red")).Should().BeEmpty("a second incident inside the cooldown stays silent");
        run.Next(Health("green")).Should().BeEmpty("and so does its recovery");

        run.Next(Health("red"));
        run.Next(Health("red")).Should().BeEmpty();
        run.Advance(TimeSpan.FromHours(1));
        run.Next(Health("red")).Should().ContainSingle("still failing once the cooldown passed")
            .Which.Kind.Should().Be(HealthAlertChangeKind.Failing);
    }

    [Fact]
    public void TheObservingPass_CountsStreaks_ButOpensClosesAndAnnouncesNothing()
    {
        var run = new Run();

        run.Next(Health("red"), observeOnly: true).Should().BeEmpty();
        run.Next(Health("red")).Should().ContainSingle("the observed red counted toward the two");

        run.Next(Health("green"), observeOnly: true).Should().BeEmpty("a boot does not close an incident");
        run.State!.Tracked.Single(entry => entry.Key == "dataQuality").Open.Should().BeTrue();
        run.Next(Health("green")).Should().ContainSingle().Which.Kind.Should().Be(HealthAlertChangeKind.Recovered);
    }

    [Fact]
    public void StreaksSavedBeforeALongPause_NoLongerCount()
    {
        var run = new Run();
        run.Next(Health("red"));

        run.Advance(TimeSpan.FromMinutes(30));
        run.Next(Health("red")).Should().BeEmpty("the earlier red is too old to be half of this incident");
        run.Next(Health("red")).Should().ContainSingle();
    }

    [Fact]
    public void AProcess_IsAnnouncedWhenItsFailureStreakCrossesTheThreshold()
    {
        var run = new Run();

        run.Next(Health("green", ("MatchIngestion", 2))).Should().BeEmpty();
        var crossed = run.Next(Health("green", ("MatchIngestion", 3)));
        run.Next(Health("green", ("MatchIngestion", 4))).Should().BeEmpty();
        var recovered = run.Next(Health("green", ("MatchIngestion", 0)));

        var failing = crossed.Should().ContainSingle().Subject;
        failing.Key.Should().Be("process:MatchIngestion");
        failing.Title.Should().Be("MatchIngestion");
        failing.Headline.Should().Be("3 consecutive failed runs. Last run: Failed.");
        recovered.Should().ContainSingle().Which.Kind.Should().Be(HealthAlertChangeKind.Recovered);
    }

    [Fact]
    public void MarkUndelivered_LetsTheNextEvaluationAnnounceAgain()
    {
        var run = new Run();
        run.Next(Health("red"));
        var opened = run.Next(Health("red"));

        HealthAlertTransitions.MarkUndelivered(run.State!, opened);

        var entry = run.State!.Tracked.Single(tracked => tracked.Key == "dataQuality");
        entry.Announced.Should().BeFalse();
        entry.LastAnnouncedAtUtc.Should().BeNull();
        run.Next(Health("red")).Should().ContainSingle("the refused alert is retried");
    }

    [Fact]
    public void Decide_LeavesThePreviousStateUntouched()
    {
        var run = new Run();
        run.Next(Health("red"));
        var before = run.State!;

        HealthAlertTransitions.Decide(before, HealthAlertTransitions.Observe(Health("red"), Options), Start, Options, observeOnly: false);

        before.Tracked.Single(entry => entry.Key == "dataQuality").FailingStreak.Should().Be(1);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("not a url", false)]
    [InlineData("ftp://discord.test/hook", false)]
    [InlineData("https://discord.test/api/webhooks/1/x", true)]
    public void Enabled_NeedsAnAbsoluteHttpWebhook(string? webhook, bool enabled)
        => new AlertingOptions { DiscordWebhookUrl = webhook }.Enabled.Should().Be(enabled);

    internal static PipelineHealthReadModel Health(string dataQuality, params (string Name, int Failures)[] processes) => new()
    {
        Status = dataQuality,
        Headline = $"Verdict {dataQuality}",
        EvaluatedAtUtc = Start,
        Signals =
        [
            new PipelineHealthSignalReadModel
            {
                Key = "dataQuality",
                Title = "Data quality",
                Status = dataQuality,
                Headline = $"dataQuality is {dataQuality}",
                UnknownReason = dataQuality == "unknown" ? "Not measured." : null,
                DetailPath = "/data-quality"
            }
        ],
        Processes =
        [
            .. processes.Select(process => new ProcessHealthReadModel
            {
                ProcessName = process.Name,
                Status = process.Failures > 0 ? "Failed" : "Success",
                ConsecutiveFailures = process.Failures
            })
        ]
    };

    /// <summary>Feeds evaluations one interval apart, carrying the state like the alerter does.</summary>
    private sealed class Run
    {
        private DateTime _now = Start;

        public HealthAlertStateDocument? State { get; private set; }

        public void Advance(TimeSpan by) => _now += by;

        public IReadOnlyList<HealthAlertChange> Next(PipelineHealthReadModel health, bool observeOnly = false)
        {
            _now += Options.EvaluationInterval;
            var decision = HealthAlertTransitions.Decide(
                State,
                HealthAlertTransitions.Observe(health, Options),
                _now,
                Options,
                observeOnly);
            State = decision.State;
            return decision.Changes;
        }
    }
}
