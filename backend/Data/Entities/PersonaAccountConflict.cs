namespace Data.Entities;

/// <summary>
/// A Riot account one persona claims while another already holds it (#2253). The persona intake
/// never moves an account on its own: it records the claim here for an operator to settle from the
/// admin, and a repeat of the same claim only bumps <see cref="LastSeenAtUtc"/> and
/// <see cref="TimesSeen"/>.
/// </summary>
/// <remarks>
/// One row per claiming persona and Riot ID (case-insensitive, like Riot), enforced by the
/// functional unique index <c>IX_persona_account_conflicts_claimant_riot_id_lower</c> that lives in
/// the AddPersonaConflicts migration only. A settled claim is not raised again: the resolution
/// writes a <see cref="PersonaAccountExclusion"/> for the persona that lost the account.
/// </remarks>
public class PersonaAccountConflict
{
    public Guid Id { get; set; }

    /// <summary>The persona whose source lists the account.</summary>
    public Guid ClaimantPersonaId { get; set; }

    public Persona? Claimant { get; set; }

    /// <summary>The persona holding the account when the claim was last seen.</summary>
    public Guid OwnerPersonaId { get; set; }

    public Persona? Owner { get; set; }

    /// <summary>The Riot ID as the claiming source published it.</summary>
    public required string GameName { get; set; }

    public required string TagLine { get; set; }

    public required string PlatformId { get; set; }

    /// <summary>The tracked account the Riot ID answers to; null while TrueMain does not track it.</summary>
    public Guid? RiotAccountId { get; set; }

    public RiotAccount? RiotAccount { get; set; }

    /// <summary>The source of the claim.</summary>
    public PersonaSource Source { get; set; }

    public PersonaConflictStatus Status { get; set; }

    /// <summary>How the operator settled it; null while <see cref="Status"/> is Open.</summary>
    public PersonaConflictResolution? Resolution { get; set; }

    public DateTime FirstSeenAtUtc { get; set; }

    public DateTime LastSeenAtUtc { get; set; }

    public int TimesSeen { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }
}
