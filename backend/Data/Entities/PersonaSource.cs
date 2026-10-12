namespace Data.Entities;

/// <summary>
/// The public source that published a persona, one of its links or one of its accounts. Stored as
/// text: an operator reads it to judge — or take down — a link (enum persistence rule, #1251).
/// </summary>
public enum PersonaSource
{
    Manual,
    Leaguepedia,
    LoLPros,
    TrackingThePros,
    Dpm,
    DeepLol,
}
