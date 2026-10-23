namespace Data.Entities;

/// <summary>
/// A patch whose build-level champion aggregates were purged (#1958): its patterns and
/// every per-patch panel table are gone, its account-level scopes and its light record
/// (<see cref="ChampionPatchStat"/>) are kept. Readers that resolve or list a patch to
/// show builds skip these, since the scopes alone would still offer a patch with
/// nothing left to render.
/// </summary>
public class ChampionBuildPurgedPatch
{
    /// <summary>The patch, <c>major.minor</c>, as the scopes store it.</summary>
    public string Patch { get; set; } = string.Empty;

    public DateTime PurgedAtUtc { get; set; }
}
