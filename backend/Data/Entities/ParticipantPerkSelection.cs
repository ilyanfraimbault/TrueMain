namespace Data.Entities;

/// <summary>
/// One rune selection of one participant. Keyed by (MatchId, ParticipantId, PerkSelectionCatalogId)
/// — no surrogate id (#124).
/// </summary>
public class ParticipantPerkSelection
{
    public string MatchId { get; set; } = string.Empty;

    public int ParticipantId { get; set; }

    public int PerkSelectionCatalogId { get; set; }

    public PerkSelectionCatalog Catalog { get; set; } = null!;
}
