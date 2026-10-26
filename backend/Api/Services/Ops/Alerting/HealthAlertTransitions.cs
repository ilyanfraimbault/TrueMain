using System.Globalization;
using Data.Ops.Mongo;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Alerting;

/// <summary>
/// The judgement half of the health alerter (#2292): from the previous state and one cockpit
/// evaluation, which incidents open, which close, and which of those are worth a message. Pure
/// and clock-free, like <see cref="Health.PipelineHealthEvaluator"/>, because the rules below
/// are the part that gets argued about.
///
/// <para>
/// <b>Transitions only.</b> A key — a cockpit signal, or a process by name — is announced when
/// its incident opens and again when it closes, never while it stays put. A signal's incident
/// opens once it read red for <see cref="AlertingOptions.RedConfirmations"/> evaluations in a
/// row; a process's once its failure streak reaches
/// <see cref="AlertingOptions.ProcessFailureThreshold"/> (the streak is its own debounce). It
/// closes on the first evaluation that reads it healthy again. <c>unknown</c> — not measured —
/// neither opens nor closes anything.
/// </para>
///
/// <para>
/// <b>Cooldown.</b> An incident opening within <see cref="AlertingOptions.Cooldown"/> of the
/// key's previous announcement stays silent, and so does its recovery: a flapping signal costs
/// one message per cooldown. If it is still open once the cooldown has passed, it is announced
/// then.
/// </para>
///
/// <para>
/// <b>Observing.</b> The first evaluation after boot only counts streaks: it opens, closes and
/// announces nothing, because a freshly started host's first reads are the least trustworthy
/// ones. State older than <see cref="StaleAfterIntervals"/> intervals has its streaks reset — a
/// red counted before a long pause is not half of an incident now.
/// </para>
/// </summary>
internal static class HealthAlertTransitions
{
    /// <summary>The prefix that keeps process keys apart from signal keys.</summary>
    public const string ProcessKeyPrefix = "process:";

    /// <summary>Evaluation intervals after which saved streaks no longer count.</summary>
    public const int StaleAfterIntervals = 3;

    private const string Red = "red";
    private const string Unknown = "unknown";

    /// <summary>What one evaluation says about every watched key.</summary>
    public static IReadOnlyList<HealthObservation> Observe(PipelineHealthReadModel health, AlertingOptions options)
    {
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(options);

        var observations = new List<HealthObservation>(health.Signals.Count + health.Processes.Count);
        foreach (var signal in health.Signals)
        {
            bool? failing = signal.Status switch
            {
                Red => true,
                Unknown => null,
                _ => false
            };
            observations.Add(new HealthObservation(
                signal.Key,
                signal.Title,
                failing,
                signal.Status,
                signal.UnknownReason ?? signal.Headline,
                options.RedConfirmations));
        }

        foreach (var process in health.Processes)
        {
            var failures = process.ConsecutiveFailures;
            observations.Add(new HealthObservation(
                ProcessKeyPrefix + process.ProcessName,
                process.ProcessName,
                failures >= options.ProcessFailureThreshold,
                process.Status,
                failures == 0
                    ? $"Last run: {process.Status}."
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"{failures} consecutive failed runs. Last run: {process.Status}."),
                Confirmations: 1));
        }

        return observations;
    }

    /// <summary>
    /// Applies one evaluation to <paramref name="previous"/> (null when none was ever saved)
    /// and returns the new state with the changes to announce. <paramref name="previous"/> is
    /// not modified.
    /// </summary>
    public static HealthAlertDecision Decide(
        HealthAlertStateDocument? previous,
        IReadOnlyList<HealthObservation> observations,
        DateTime nowUtc,
        AlertingOptions options,
        bool observeOnly)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(options);

        var stale = previous?.LastEvaluatedAtUtc is not { } last
                    || nowUtc - last > options.EvaluationInterval * StaleAfterIntervals;
        var tracked = (previous?.Tracked ?? []).ToDictionary(entry => entry.Key, Copy, StringComparer.Ordinal);
        var changes = new List<HealthAlertChange>();

        foreach (var observation in observations)
        {
            if (!tracked.TryGetValue(observation.Key, out var entry))
            {
                entry = new HealthAlertTrackedDocument { Key = observation.Key };
                tracked[observation.Key] = entry;
            }
            else if (stale)
            {
                entry.FailingStreak = 0;
            }

            entry.LastStatus = observation.Status;
            var change = observation.Failing switch
            {
                true => OnFailing(entry, observation, nowUtc, options, observeOnly),
                false => OnHealthy(entry, observation, observeOnly),
                null => null
            };
            if (change is not null)
            {
                changes.Add(change);
            }
        }

        var state = new HealthAlertStateDocument
        {
            Version = previous?.Version ?? 0,
            LastEvaluatedAtUtc = nowUtc,
            Tracked = [.. tracked.Values.OrderBy(entry => entry.Key, StringComparer.Ordinal)]
        };
        return new HealthAlertDecision(state, changes);
    }

    /// <summary>
    /// Undoes the announcements of <paramref name="changes"/> in <paramref name="state"/> after
    /// the channel refused them, so the next evaluation tries again instead of treating the
    /// incident as announced. A recovery that failed to send is not retried: the incident is
    /// closed either way.
    /// </summary>
    public static void MarkUndelivered(HealthAlertStateDocument state, IReadOnlyList<HealthAlertChange> changes)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(changes);

        foreach (var change in changes.Where(change => change.Kind == HealthAlertChangeKind.Failing))
        {
            var entry = state.Tracked.FirstOrDefault(tracked => tracked.Key == change.Key);
            if (entry is not null)
            {
                entry.Announced = false;
                entry.LastAnnouncedAtUtc = change.PreviousAnnouncedAtUtc;
            }
        }
    }

    private static HealthAlertChange? OnFailing(
        HealthAlertTrackedDocument entry,
        HealthObservation observation,
        DateTime nowUtc,
        AlertingOptions options,
        bool observeOnly)
    {
        if (entry.FailingStreak < int.MaxValue)
        {
            entry.FailingStreak++;
        }

        if (observeOnly)
        {
            return null;
        }

        if (!entry.Open && entry.FailingStreak >= observation.Confirmations)
        {
            entry.Open = true;
            entry.Announced = false;
            entry.OpenedAtUtc = nowUtc;
        }

        var cooledDown = entry.LastAnnouncedAtUtc is not { } lastAnnounced
                         || nowUtc - lastAnnounced >= options.Cooldown;
        if (!entry.Open || entry.Announced || !cooledDown)
        {
            return null;
        }

        var previousAnnouncement = entry.LastAnnouncedAtUtc;
        entry.Announced = true;
        entry.LastAnnouncedAtUtc = nowUtc;
        return new HealthAlertChange(
            observation.Key,
            observation.Title,
            HealthAlertChangeKind.Failing,
            observation.Status,
            observation.Headline,
            previousAnnouncement);
    }

    private static HealthAlertChange? OnHealthy(
        HealthAlertTrackedDocument entry,
        HealthObservation observation,
        bool observeOnly)
    {
        entry.FailingStreak = 0;
        if (observeOnly || !entry.Open)
        {
            return null;
        }

        var wasAnnounced = entry.Announced;
        entry.Open = false;
        entry.Announced = false;
        entry.OpenedAtUtc = null;
        return wasAnnounced
            ? new HealthAlertChange(
                observation.Key,
                observation.Title,
                HealthAlertChangeKind.Recovered,
                observation.Status,
                observation.Headline,
                entry.LastAnnouncedAtUtc)
            : null;
    }

    private static HealthAlertTrackedDocument Copy(HealthAlertTrackedDocument entry) => new()
    {
        Key = entry.Key,
        LastStatus = entry.LastStatus,
        FailingStreak = entry.FailingStreak,
        Open = entry.Open,
        Announced = entry.Announced,
        OpenedAtUtc = entry.OpenedAtUtc,
        LastAnnouncedAtUtc = entry.LastAnnouncedAtUtc
    };
}

/// <summary>
/// One watched key as one evaluation saw it. <see cref="Failing"/> is null when the key was
/// not measured, which neither opens nor closes an incident.
/// </summary>
internal sealed record HealthObservation(
    string Key,
    string Title,
    bool? Failing,
    string Status,
    string Headline,
    int Confirmations);

internal enum HealthAlertChangeKind
{
    Failing,
    Recovered
}

/// <summary>
/// One key to announce. <see cref="PreviousAnnouncedAtUtc"/> is the announcement before this
/// one, kept so an undelivered alert can be rolled back.
/// </summary>
internal sealed record HealthAlertChange(
    string Key,
    string Title,
    HealthAlertChangeKind Kind,
    string Status,
    string Headline,
    DateTime? PreviousAnnouncedAtUtc);

internal sealed record HealthAlertDecision(HealthAlertStateDocument State, IReadOnlyList<HealthAlertChange> Changes);
