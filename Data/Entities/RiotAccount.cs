namespace Data.Entities;

public class RiotAccount
{
    public Guid Id { get; set; }

    public string Puuid { get; set; } = string.Empty;

    public string GameName { get; set; } = string.Empty;

    public string? TagLine { get; set; }

    public string PlatformId { get; set; } = string.Empty;

    public Guid? PersonaId { get; set; }

    public Persona? Persona { get; set; }

    public string? SummonerId { get; set; }

    public int ProfileIconId { get; set; }

    public int SummonerLevel { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? LastProfileSyncAtUtc { get; set; }

    public DateTime? LastMainCalcAtUtc { get; set; }
}
