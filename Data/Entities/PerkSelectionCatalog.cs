namespace Data.Entities;

public class PerkSelectionCatalog
{
    public int Id { get; set; }

    public int StyleId { get; set; }

    public int SelectionIndex { get; set; }

    public int PerkId { get; set; }

    public string StyleDescription { get; set; } = string.Empty;
}
