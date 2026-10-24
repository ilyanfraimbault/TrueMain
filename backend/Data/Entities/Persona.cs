namespace Data.Entities;

/// <summary>
/// A known player — a pro, a streamer, a content creator, or simply a notable player — who plays on
/// one or several Riot accounts (#2016). The site shows it under the person's own name at
/// <c>/players/{slug}</c>; "persona" is the code's word for it, never the UI's.
/// </summary>
/// <remarks>
/// Fed by the ops write path (<c>POST /ops/personas/batch</c>) from public sources only: every
/// account and link carries the source that published it, and nothing here is inferred by TrueMain
/// (Riot's rule against de-anonymising players).
/// </remarks>
public class Persona
{
    public Guid Id { get; set; }

    /// <summary>URL key, lowercase kebab (<c>irelking</c>). Unique; never reused for another person.</summary>
    public required string Slug { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>
    /// True once an operator set <see cref="DisplayName"/> (#2019): created the persona from the admin,
    /// or renamed it. The intake then keeps the operator's name instead of the one each source sends.
    /// </summary>
    public bool DisplayNameLocked { get; set; }

    /// <summary>The source that first published this person.</summary>
    public PersonaSource Source { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// The persona an operator merged this one into (#2253); null for a live persona. A merged
    /// persona keeps its row and slug but no account or link: <c>GET /players/{slug}</c> answers the
    /// persona it points to, so the page redirects there, and the intake feeds that persona when a
    /// source sends the old slug — the scrapers never recreate it. Always points at a live persona:
    /// a merge re-points the personas already merged into the one it absorbs.
    /// </summary>
    public Guid? MergedIntoPersonaId { get; set; }

    public Persona? MergedInto { get; set; }

    /// <summary>
    /// When an operator removed this persona (#2019); null for a live one. A removed persona keeps its
    /// row and slug but no account or link, so <c>GET /players/{slug}</c> answers 404 and the intake
    /// answers <c>removed</c> for its slug and writes nothing: the scrapers never recreate it. A
    /// takedown also bars its Riot accounts from every other persona
    /// (<see cref="PersonaAccountExclusion.IsTakedown"/>).
    /// </summary>
    public DateTime? RemovedAtUtc { get; set; }

    public ICollection<PersonaLink> Links { get; set; } = new List<PersonaLink>();

    public ICollection<PersonaAccount> Accounts { get; set; } = new List<PersonaAccount>();
}
