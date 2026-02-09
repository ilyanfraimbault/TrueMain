namespace Data.Entities;

public class ParticipantPerkSelection
{
    public Guid Id { get; set; }

    public string MatchId { get; set; } = string.Empty;

    public int ParticipantId { get; set; }

    public int PerkSelectionCatalogId { get; set; }
}
