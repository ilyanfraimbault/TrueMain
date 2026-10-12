namespace Data.Entities;

/// <summary>A public channel or page of a <see cref="Persona"/> — a Twitch channel, a YouTube channel, a wiki page.</summary>
public class PersonaLink
{
    public Guid Id { get; set; }

    public Guid PersonaId { get; set; }

    public Persona? Persona { get; set; }

    public PersonaLinkKind Kind { get; set; }

    /// <summary>Absolute <c>https</c> URL; unique per persona.</summary>
    public required string Url { get; set; }

    public PersonaSource Source { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
