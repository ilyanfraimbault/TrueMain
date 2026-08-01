using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

public interface ITruemainActivityQueryService
{
    /// <summary>
    /// Returns the activity-grid payload for <paramref name="nameTag"/>
    /// (<c>gameName-tagLine</c>): the per-game / per-day / per-week foldings of the
    /// player's retained ranked games plus the per-patch history of their signature
    /// champion. Returns <see langword="null"/> when the name tag is malformed or
    /// no Riot account matches — the controller maps that to 404.
    /// </summary>
    /// <remarks>
    /// Every other outcome is a 200. A player with nothing left inside the match
    /// retention window gets three empty match-sourced series and (usually) a
    /// populated patch series; a player with no classified main gets the mirror
    /// case. Both are real answers about a real account, and the read models carry
    /// the source / scope / coverage the UI needs to say which.
    /// </remarks>
    Task<TruemainActivityReadModel?> GetAsync(string nameTag, CancellationToken ct);
}
