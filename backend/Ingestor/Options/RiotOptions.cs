namespace Ingestor.Options;

public class RiotOptions
{
    public string ApiKey { get; set; } = string.Empty;

    public int MaxRetryAttempts { get; set; } = 3;
}
