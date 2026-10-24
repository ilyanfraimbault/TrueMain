namespace Data.Entities;

/// <summary>
/// A Riot account one persona may not claim any more: an operator settled a conflict against it
/// (#2253) or unlinked the account from it (#2019). The persona intake answers <c>excluded</c> for that claim and writes nothing, so the
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

    /// <summary>
    /// Written by a takedown (#2019): the removed persona's accounts are barred from every persona,
    /// not only from the one that held them, so another source listing the same person under
    /// another slug cannot put them back on the site.
    /// </summary>
    public bool IsTakedown { get; set; }

    /// <summary>The conflict whose resolution wrote it; null for an operator's unlink or a takedown.</summary>
    public Guid? ConflictId { get; set; }

    public PersonaAccountConflict? Conflict { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
