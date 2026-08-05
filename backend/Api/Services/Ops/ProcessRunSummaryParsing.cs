using System.Text.Json;

namespace TrueMain.Services.Ops;

/// <summary>
/// Parses the raw JSON text a process-run document carries (see
/// <c>ProcessRunDocument.SummaryJson</c>) into the detached
/// <see cref="JsonElement"/> the read models expose. Malformed text (which only a
/// hand-edited document could produce) surfaces as a null summary rather than a
/// 500 on every panel load.
/// </summary>
internal static class ProcessRunSummaryParsing
{
    public static JsonElement? Parse(string? summaryJson)
    {
        if (string.IsNullOrWhiteSpace(summaryJson))
        {
            return null;
        }

        try
        {
            // Clone the root so the value is detached from the JsonDocument's
            // lifetime; System.Text.Json then writes it as raw JSON in the response.
            using var document = JsonDocument.Parse(summaryJson);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The parsed summary object, or null when the run recorded none, recorded
    /// malformed text, or recorded something that is not a JSON object (a shape no
    /// summary record produces). Unlike <see cref="Parse"/> this does <em>not</em> clone:
    /// the caller reads a few counters and disposes, which is what the counter series
    /// want — cloning every summary across a 180-day window would deep-copy documents
    /// only to read three numbers out of each.
    /// </summary>
    public static JsonDocument? TryParseObject(string? summaryJson)
    {
        if (string.IsNullOrWhiteSpace(summaryJson))
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(summaryJson);
        }
        catch (JsonException)
        {
            // A malformed summary is a missing summary, not a reason to fail the
            // whole panel.
            return null;
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            return document;
        }

        document.Dispose();
        return null;
    }

    /// <summary>
    /// A counter off a summary object, 0 when the key is absent or not a number. Absent
    /// and zero are deliberately the same here: callers that must tell them apart (a
    /// forward-only counter, #1024) probe the key themselves.
    /// </summary>
    public static long ReadInt64(JsonElement element, string property)
        => element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt64(out var number)
            ? number
            : 0;
}
