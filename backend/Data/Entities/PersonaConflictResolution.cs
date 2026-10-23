namespace Data.Entities;

/// <summary>How an operator settled a <see cref="PersonaAccountConflict"/>. Stored as text (#1251).</summary>
public enum PersonaConflictResolution
{
    /// <summary>The account went to the claiming persona; the former holder may not claim it back.</summary>
    Moved,

    /// <summary>The account stayed with its holder; the claimant may not claim it again.</summary>
    Kept,

    /// <summary>The account belongs to neither: removed from the holder, barred for both.</summary>
    Detached,

    /// <summary>The two personas were one person: merged into the one the operator kept.</summary>
    Merged,
}
