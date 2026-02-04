namespace Data.Entities;

public class Player
{
    public Guid Id { get; set; }

    public string GameName { get; set; } = string.Empty;

    public string TagLine { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string Puuid { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
