using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Data.Ops.Mongo;

/// <summary>
/// The health alerter's memory (#2292), one document in <c>health_alert_state</c>: what each
/// watched signal and process looked like at the last evaluation, so a deploy neither sends an
/// alert again for an incident already announced nor forgets one still open.
/// </summary>
public sealed class HealthAlertStateDocument
{
    /// <summary>The single document's id.</summary>
    public const string SingletonId = "pipeline-health";

    [BsonId]
    public string Id { get; set; } = SingletonId;

    /// <summary>
    /// Bumped on every save, and the save is conditioned on it: two Api containers evaluating
    /// at once during a rolling deploy cannot both claim one transition and both announce it.
    /// </summary>
    [BsonElement("version")]
    public long Version { get; set; }

    [BsonElement("lastEvaluatedAtUtc")]
    [BsonIgnoreIfNull]
    public DateTime? LastEvaluatedAtUtc { get; set; }

    /// <summary>One entry per watched signal (<c>dataQuality</c>…) and process (<c>process:Name</c>).</summary>
    [BsonElement("tracked")]
    public List<HealthAlertTrackedDocument> Tracked { get; set; } = [];
}

/// <summary>One watched signal or process inside <see cref="HealthAlertStateDocument"/>.</summary>
public sealed class HealthAlertTrackedDocument
{
    /// <summary>The signal key (<c>dataQuality</c>) or <c>process:</c> plus the process name.</summary>
    [BsonElement("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>The status last observed, as the cockpit spells it.</summary>
    [BsonElement("lastStatus")]
    public string LastStatus { get; set; } = string.Empty;

    /// <summary>Consecutive evaluations seen failing, the debounce counter.</summary>
    [BsonElement("failingStreak")]
    public int FailingStreak { get; set; }

    /// <summary>True while an incident is open: the failure held long enough to count.</summary>
    [BsonElement("open")]
    public bool Open { get; set; }

    /// <summary>True once the open incident was announced, so its recovery is announced too.</summary>
    [BsonElement("announced")]
    public bool Announced { get; set; }

    [BsonElement("openedAtUtc")]
    [BsonIgnoreIfNull]
    public DateTime? OpenedAtUtc { get; set; }

    /// <summary>The last time this key's failure was announced, which its cooldown counts from.</summary>
    [BsonElement("lastAnnouncedAtUtc")]
    [BsonIgnoreIfNull]
    public DateTime? LastAnnouncedAtUtc { get; set; }
}

/// <summary>One alert the Api sent (#2292), persisted in <c>health_alerts</c> for the <c>/health</c> panel.</summary>
public sealed class HealthAlertDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("sentAtUtc")]
    public DateTime SentAtUtc { get; set; }

    /// <summary><c>failing</c> when the alert announces at least one failure, <c>recovered</c> otherwise.</summary>
    [BsonElement("kind")]
    public string Kind { get; set; } = string.Empty;

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>The cockpit verdict when the alert was sent.</summary>
    [BsonElement("verdict")]
    public string Verdict { get; set; } = string.Empty;

    /// <summary>The channel the alert went to, e.g. <c>discord</c>.</summary>
    [BsonElement("channel")]
    public string Channel { get; set; } = string.Empty;

    /// <summary>False when the channel refused it or could not be reached; <see cref="Error"/> says why.</summary>
    [BsonElement("delivered")]
    public bool Delivered { get; set; }

    /// <summary>Why delivery failed — a status code or an exception type, never the channel's URL.</summary>
    [BsonElement("error")]
    [BsonIgnoreIfNull]
    public string? Error { get; set; }

    [BsonElement("changes")]
    public List<HealthAlertChangeDocument> Changes { get; set; } = [];
}

/// <summary>One signal or process an alert reports on.</summary>
public sealed class HealthAlertChangeDocument
{
    [BsonElement("key")]
    public string Key { get; set; } = string.Empty;

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary><c>failing</c> or <c>recovered</c>.</summary>
    [BsonElement("change")]
    public string Change { get; set; } = string.Empty;

    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;

    [BsonElement("headline")]
    public string Headline { get; set; } = string.Empty;
}
