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
}
