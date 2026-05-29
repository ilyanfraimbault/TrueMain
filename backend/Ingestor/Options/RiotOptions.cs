namespace Ingestor.Options;

public class RiotOptions
{
    public const string SectionName = "Riot";

    public string ApiKey { get; set; } = string.Empty;

    public int MaxRetryAttempts { get; set; } = 3;
}
