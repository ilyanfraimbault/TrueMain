namespace Ingestor.Options;

public class SeedOptions
{
    public const string SectionName = "Seed";

    public List<string> MatchIds { get; set; } = new();
}
