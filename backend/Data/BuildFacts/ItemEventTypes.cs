namespace Data.BuildFacts;

/// <summary>
/// The Riot timeline item-event types the build-fact resolvers react to, plus the one
/// allocation-free way to classify a raw <see cref="Entities.ItemEvent.EventType"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BootsResolver"/>, <see cref="FinalBuildResolver"/> and
/// <see cref="StarterItemAnalyzer"/> each fold the <c>ItemEvents</c> jsonb of every
/// participant of every match on every aggregation cycle, so this classification sits on
/// the hottest loop of the pipeline. The three of them used to switch on
/// <c>EventType.ToUpperInvariant()</c> — one string allocated per event, thrown away
/// immediately — while another comparison in the same file already used the allocation-free
/// <see cref="string.Equals(string, System.StringComparison)"/>. Both styles are now this one.
/// </para>
/// <para>
/// The spellings live here rather than as literals in each resolver so the three cannot
/// drift on which events they recognise: a starter basket that honours <c>ITEM_UNDO</c>
/// while the final build ignores it would silently disagree about the same game.
/// </para>
/// </remarks>
public static class ItemEventTypes
{
    public const string Purchased = "ITEM_PURCHASED";
    public const string Sold = "ITEM_SOLD";
    public const string Destroyed = "ITEM_DESTROYED";
    public const string Undo = "ITEM_UNDO";

    /// <summary>
    /// Maps a raw Riot event type onto the kind the resolvers dispatch on. Comparison is
    /// ordinal case-insensitive: Riot sends these upper-cased, but the jsonb column has
    /// carried whatever the timeline ingestion wrote since before that was guaranteed.
    /// Anything unrecognised is <see cref="ItemEventKind.Other"/> — the resolvers ignore it.
    /// </summary>
    public static ItemEventKind Classify(string? eventType)
    {
        if (string.IsNullOrEmpty(eventType))
        {
            return ItemEventKind.Other;
        }

        if (eventType.Equals(Purchased, StringComparison.OrdinalIgnoreCase))
        {
            return ItemEventKind.Purchased;
        }

        if (eventType.Equals(Sold, StringComparison.OrdinalIgnoreCase))
        {
            return ItemEventKind.Sold;
        }

        if (eventType.Equals(Destroyed, StringComparison.OrdinalIgnoreCase))
        {
            return ItemEventKind.Destroyed;
        }

        if (eventType.Equals(Undo, StringComparison.OrdinalIgnoreCase))
        {
            return ItemEventKind.Undo;
        }

        return ItemEventKind.Other;
    }
}

/// <summary>
/// The item-event kinds the build-fact resolvers dispatch on. <see cref="Other"/> covers
/// every timeline event none of them acts upon.
/// </summary>
public enum ItemEventKind
{
    Other = 0,
    Purchased,
    Sold,
    Destroyed,
    Undo
}
