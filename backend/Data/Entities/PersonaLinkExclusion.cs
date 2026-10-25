namespace Data.Entities;

/// <summary>
/// A link an operator removed from a persona (#2019). The intake skips that URL when a source sends
/// it again, so the weekly scrapers, which only ever add links, cannot put it back. Adding the
/// same URL from the admin lifts it.
/// </summary>
public class PersonaLinkExclusion
{
    public Guid Id { get; set; }

    public Guid PersonaId { get; set; }

    public Persona? Persona { get; set; }

    /// <summary>The normalised URL, as <see cref="PersonaLink.Url"/> stored it.</summary>
    public required string Url { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
