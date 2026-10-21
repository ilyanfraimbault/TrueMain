namespace Data.Statics;

/// <summary>
/// Per-patch shop classes of the game's items (<see cref="ItemShopClass"/>), keyed by item id.
/// </summary>
public interface IItemClassProvider
{
    /// <summary>
    /// The classes of every item that has one, for the patch <paramref name="gameVersion"/>
    /// belongs to (the newest published data when null), each in the game data's order — the
    /// item's main class first. Throws when the source is unreachable.
    /// </summary>
    Task<ItemClasses> GetClassesAsync(string? gameVersion, CancellationToken ct);
}

/// <summary>
/// The shop classes of a patch's items.
/// </summary>
/// <param name="Patch">
/// The data's <c>major.minor</c> patch, or <c>latest</c> when none was asked for or the asked
/// one is not published yet.
/// </param>
/// <param name="Items">Each item's classes, main class first; items without a class are left out.</param>
public sealed record ItemClasses(string Patch, IReadOnlyDictionary<int, IReadOnlyList<ItemShopClass>> Items);
