namespace TrueMain.ReadModels.Ops;

/// <summary>
/// One page of the operator-action trail for the admin Audit page and the known-player History
/// (#2288), newest first. The catalogs ride on every page so the filters need no extra call.
/// </summary>
public sealed record AuditEventsReadModel
{
    public IReadOnlyList<AuditEventReadModel> Entries { get; init; } = [];

    public long Total { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    /// <summary>Every action name a writer records, with its plain words.</summary>
    public IReadOnlyList<AuditActionReadModel> Actions { get; init; } = [];

    /// <summary>Who can act: <c>operator</c>, <c>auto</c>, <c>ingestor</c>.</summary>
    public IReadOnlyList<string> Actors { get; init; } = [];

    /// <summary>The kinds of entity an event targets.</summary>
    public IReadOnlyList<string> TargetTypes { get; init; } = [];
}

/// <summary>An action name and the words the admin shows for it.</summary>
public sealed record AuditActionReadModel(string Name, string Label);

/// <summary>One operator action: who, what (<see cref="Label"/> in plain words), on what, and its context.</summary>
public sealed record AuditEventReadModel
{
    public string Id { get; init; } = string.Empty;

    public DateTime TimestampUtc { get; init; }

    public string Actor { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string? TargetType { get; init; }

    public string? TargetId { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
