namespace TrueMain.Requests.Ops;

/// <summary>Body of <c>POST /ops/personas/homonyms/merge</c> (#2271): known players that are one person.</summary>
public sealed record PersonaHomonymMergeRequest
{
    /// <summary>The persona that stays.</summary>
    public Guid? KeepId { get; init; }

    /// <summary>The personas folded into it; their slugs redirect to it from then on.</summary>
    public IReadOnlyList<Guid>? MergeIds { get; init; }
}

/// <summary>Body of <c>POST /ops/personas/homonyms/distinct</c> (#2271): known players that are different people.</summary>
public sealed record PersonaHomonymDistinctRequest
{
    /// <summary>Two or more personas; every pair among them is marked.</summary>
    public IReadOnlyList<Guid>? PersonaIds { get; init; }
}
