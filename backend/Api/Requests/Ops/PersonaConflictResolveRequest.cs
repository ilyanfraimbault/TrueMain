using TrueMain.ReadModels.Ops;

namespace TrueMain.Requests.Ops;

/// <summary>
/// Body of <c>POST /ops/personas/conflicts/{id}/resolve</c> (#2253). <see cref="Action"/> is one of
/// <c>move</c> (the account goes to the claiming persona), <c>keep</c> (it stays with its holder),
/// <c>detach</c> (it belongs to neither) or <c>merge</c> (the two personas are one person).
/// </summary>
public sealed record PersonaConflictResolveRequest
{
    public PersonaConflictAction? Action { get; init; }

    /// <summary>For <c>merge</c> only: <c>claimant</c> or <c>owner</c>, the persona that stays.</summary>
    public PersonaConflictSide? Keep { get; init; }
}
