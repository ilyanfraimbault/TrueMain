using Core;

namespace Ingestor.Options;

public class RiotOptions
{
    public string ApiKey { get; set; } = string.Empty;

    public RegionalRoute RegionalRoute { get; set; } = RegionalRoute.Europe;

    public int MaxRetryAttempts { get; set; } = 3;
}
