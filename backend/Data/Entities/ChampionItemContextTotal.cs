using Data.ItemContext;

namespace Data.Entities;

/// <summary>
/// How many games a champion played in one bucket of one draft axis, whatever it built
/// (#1450) — the denominator <see cref="ChampionItemContextStat"/> is divided by. Written
/// by the same fold, from the same games, in the same transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>Per slot, not per champion.</b> A game only counts towards the slot it could be read
/// for: a game whose item metadata never resolved contributes to no slot, and a game where
/// the player finished without boots is a game the boots slot cannot be a rate over. Keeping
/// the slot in the key is what stops a pick rate from being divided by games in which the
/// question was never asked.
/// </para>
/// <para>
/// Unlike the ban totals this row is <em>not</em> also the champion's game count: a champion
/// plays games in which an axis could not be evaluated at all — two unprofiled enemies, no
/// 15-minute snapshot — and those games are counted in no bucket of that axis. Reading
/// <see cref="ItemContextAxis.Overall"/> is the way to ask how many games the slot saw.
/// </para>
/// </remarks>
public class ChampionItemContextTotal
{
    public Guid Id { get; set; }

    public int ChampionId { get; set; }

    public string Position { get; set; } = string.Empty;

    public string Patch { get; set; } = string.Empty;

    public ItemContextSlot Slot { get; set; }

    public ItemContextAxis Axis { get; set; }

    public ItemContextBucket Bucket { get; set; }

    public int Games { get; set; }

    public int Wins { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
