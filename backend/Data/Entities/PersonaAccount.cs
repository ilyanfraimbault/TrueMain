namespace Data.Entities;

/// <summary>
/// One Riot account a source lists for a <see cref="Persona"/>, kept as the Riot ID the source
/// published so it survives until TrueMain tracks the account.
/// </summary>
/// <remarks>
/// <see cref="RiotAccountId"/> is null while no tracked account answers to that Riot ID; the ops write
/// path resolves it — and may seed the account — each time the persona is upserted. Once resolved,
/// the link follows the account by its row, so a later rename does not break it: reads show the
/// account's current Riot ID, not the one stored here.
/// </remarks>
public class PersonaAccount
{
    public Guid Id { get; set; }

    public Guid PersonaId { get; set; }

    public Persona? Persona { get; set; }

    public required string GameName { get; set; }

    public required string TagLine { get; set; }

    public required string PlatformId { get; set; }

    public Guid? RiotAccountId { get; set; }

    public RiotAccount? RiotAccount { get; set; }

    public PersonaSource Source { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>When <see cref="RiotAccountId"/> was set; null while the account is untracked.</summary>
    public DateTime? ResolvedAtUtc { get; set; }
}
