namespace Data.Metrics;

/// <summary>
/// The names of TrueMain's own <c>System.Diagnostics.Metrics</c> meters, shared by the host
/// that declares one and the Api that reads its rollups back (#1636).
/// </summary>
public static class MeterNames
{
    /// <summary>The Ingestor's meter: run failures, Riot rate-limit waits and 429s.</summary>
    public const string Ingestor = "TrueMain.Ingestor";
}
