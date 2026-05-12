namespace Data.Entities;

public class Persona
{
    public Guid Id { get; set; }

    public string? DisplayName { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<RiotAccount> RiotAccounts { get; set; } = new List<RiotAccount>();
}
