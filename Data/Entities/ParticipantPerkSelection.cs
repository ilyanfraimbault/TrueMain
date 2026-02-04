namespace Data.Entities;

public class ParticipantPerkSelection
{
    public Guid Id { get; set; }

    public string MatchId { get; set; } = string.Empty;

    public int ParticipantId { get; set; }

    public int StyleId { get; set; }

    public string StyleDescription { get; set; } = string.Empty;

    public int SelectionIndex { get; set; }

    public int PerkId { get; set; }
}
