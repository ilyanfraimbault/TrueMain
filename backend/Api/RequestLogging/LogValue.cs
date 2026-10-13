namespace TrueMain.RequestLogging;

/// <summary>
/// Keeps a request-supplied value on one log line (#2057). The console provider writes plain
/// text, so a CR or LF inside a logged value would start a forged entry of its own in the
/// container log; the Mongo sink stores fields and is not affected either way.
/// </summary>
public static class LogValue
{
    public static string? SingleLine(string? value) =>
        value?.Replace("\r", string.Empty).Replace("\n", string.Empty);
}
