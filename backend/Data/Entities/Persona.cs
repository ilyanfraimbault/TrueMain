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

    /// <summary>The source that first published this person.</summary>
    public PersonaSource Source { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<PersonaLink> Links { get; set; } = new List<PersonaLink>();

    public ICollection<PersonaAccount> Accounts { get; set; } = new List<PersonaAccount>();
}
