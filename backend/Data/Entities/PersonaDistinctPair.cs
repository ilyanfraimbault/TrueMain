namespace Data.Entities;

/// <summary>
/// Two live personas with the same name that an operator marked as different people (#2271). The
/// admin's homonyms queue groups personas by name and leaves out a group once every pair in it is
/// marked, so the decision holds until a new persona with that name appears.
/// </summary>
/// <remarks>
/// One row per unordered pair, stored with <see cref="PersonaId"/> &lt; <see cref="OtherPersonaId"/>
/// (Guid order, as Postgres compares uuids). A merge moves the merged persona's marks to the kept one.
/// </remarks>
public class PersonaDistinctPair
{
    public Guid PersonaId { get; set; }

    public Persona? Persona { get; set; }

    public Guid OtherPersonaId { get; set; }

    public Persona? OtherPersona { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
