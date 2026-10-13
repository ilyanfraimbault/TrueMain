namespace Data.Entities;

/// <summary>What a <see cref="PersonaLink"/> points at. Stored as text, like <see cref="PersonaSource"/>.</summary>
public enum PersonaLinkKind
{
    Website,
    Twitch,
    YouTube,
    X,
    Kick,
    TikTok,
    Instagram,
    Leaguepedia,
    LoLPros,
    DeepLol,

    /// <summary>SOOP (formerly AfreecaTV), where most Korean pros stream.</summary>
    Soop,
    Chzzk,
}
