using Data.Entities;

namespace TrueMain.Services.Personas;

/// <summary>
/// How sure a conflict's answer is (#2277), from the links the two known players list. A social
/// channel both list is the strongest evidence of one person — nobody else streams on it or posts
/// from it — so it weighs most; a reference page both list (Leaguepedia, LoLPros, DeepLoL, a
/// website) is weaker, sources copying each other; a social kind both list with different channels
/// counts against. The open queue is ordered by <see cref="PersonaConflictEvidence.Score"/>, most certain first.
/// </summary>
public static class PersonaConflictConfidence
{
    public const int SharedSocialWeight = 10;
    public const int SharedReferenceWeight = 3;
    public const int DifferingSocialWeight = -5;

    private static readonly HashSet<PersonaLinkKind> SocialKinds =
    [
        PersonaLinkKind.Twitch,
        PersonaLinkKind.YouTube,
        PersonaLinkKind.X,
        PersonaLinkKind.Kick,
        PersonaLinkKind.TikTok,
        PersonaLinkKind.Instagram,
        PersonaLinkKind.Soop,
        PersonaLinkKind.Chzzk,
    ];

    public static bool IsSocial(PersonaLinkKind kind) => SocialKinds.Contains(kind);

    /// <summary>What the two link lists have in common, and what sets them apart.</summary>
    public static PersonaConflictEvidence Evaluate(
        IReadOnlyCollection<(PersonaLinkKind Kind, string Url)> claimant,
        IReadOnlyCollection<(PersonaLinkKind Kind, string Url)> owner)
    {
        var ownerKeys = owner.Select(l => (l.Kind, Key: ChannelKey(l.Url))).ToHashSet();
        var shared = claimant
            .Select(l => (l.Kind, Key: ChannelKey(l.Url)))
            .Distinct()
            .Where(ownerKeys.Contains)
            .ToList();
        var sharedKinds = shared.Select(l => l.Kind).ToHashSet();
        var differing = claimant.Select(l => l.Kind)
            .Intersect(owner.Select(l => l.Kind))
            .Where(kind => IsSocial(kind) && !sharedKinds.Contains(kind))
            .Order()
            .ToList();

        return new PersonaConflictEvidence(
            shared.Where(l => IsSocial(l.Kind)).Select(l => l.Kind).Distinct().Order().ToList(),
            shared.Count(l => !IsSocial(l.Kind)),
            differing);
    }

    /// <summary>
    /// A channel's identity across sources: decoded (one source escapes <c>Rosén</c>, another does
    /// not), lowercased, without the scheme, a <c>www.</c>, a query or a trailing slash — the admin's
    /// <c>channelKey</c>, so the queue's order and the drawer's comparison agree.
    /// </summary>
    public static string ChannelKey(string url)
    {
        var key = Uri.UnescapeDataString(url).ToLowerInvariant();
        key = WithoutPrefix(WithoutPrefix(WithoutPrefix(key, "https://"), "http://"), "www.");
        var cut = key.IndexOfAny(['?', '#']);
        return (cut >= 0 ? key[..cut] : key).TrimEnd('/');
    }

    private static string WithoutPrefix(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : value;
}

/// <summary>The links behind a conflict's confidence.</summary>
/// <param name="SharedSocial">Social kinds whose channel both list.</param>
/// <param name="SharedReferences">Reference pages both list.</param>
/// <param name="DifferingSocial">Social kinds both list with different channels.</param>
public sealed record PersonaConflictEvidence(
    IReadOnlyList<PersonaLinkKind> SharedSocial, int SharedReferences, IReadOnlyList<PersonaLinkKind> DifferingSocial)
{
    public int Score =>
        (SharedSocial.Count * PersonaConflictConfidence.SharedSocialWeight)
        + (SharedReferences * PersonaConflictConfidence.SharedReferenceWeight)
        + (DifferingSocial.Count * PersonaConflictConfidence.DifferingSocialWeight);
}
