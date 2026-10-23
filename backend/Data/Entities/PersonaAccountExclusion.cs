namespace Data.Entities;

/// <summary>
/// A Riot account one persona may not claim any more (#2253): an operator settled a conflict
/// against it. The persona intake answers <c>excluded</c> for that claim and writes nothing, so the
/// weekly scrapers, which keep sending what their sources list, cannot undo the decision.
/// </summary>
/// <remarks>
/// Matched on the Riot ID (case-insensitive, unique per persona through the functional index
/// <c>IX_persona_account_exclusions_persona_riot_id_lower</c> in the AddPersonaConflicts migration)
/// and on the tracked account, so a rename the source catches up with stays excluded.
/// </remarks>
public class PersonaAccountExclusion
{
    public Guid Id { get; set; }

    public Guid PersonaId { get; set; }

    public Persona? Persona { get; set; }

    public required string GameName { get; set; }

    public required string TagLine { get; set; }

    public required string PlatformId { get; set; }

    public Guid? RiotAccountId { get; set; }

    public RiotAccount? RiotAccount { get; set; }

    /// <summary>The conflict whose resolution wrote it.</summary>
    public Guid? ConflictId { get; set; }

    public PersonaAccountConflict? Conflict { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
