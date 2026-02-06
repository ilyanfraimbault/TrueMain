namespace Ingestor.Options;

public class JobOptions
{
    public string Mode { get; set; } = "Full";

    public bool RunOnce { get; set; } = true;

    public int? IntervalMinutes { get; set; }
}
