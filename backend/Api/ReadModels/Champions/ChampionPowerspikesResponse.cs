namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Event spikes for a champion at a position, scoped to one core build (#890):
/// the items that build completes and the level milestones (6/11/16), each
/// carrying how much the champion's opponent-relative power accelerates around
/// it — the spike. Reconstructed from the pre-aggregated powerspike stats
/// (#694); same queue / patch / tracked-account population as the sibling
/// champion reads.
///
/// The mean power curve is no longer returned: it is read server-side purely as
/// the baseline the spikes are measured against.
/// </summary>
public sealed record ChampionPowerspikesResponse
{
    public int ChampionId { get; init; }
    public string Position { get; init; } = string.Empty;
    public string? Patch { get; init; }

    /// <summary>Spike events, ordered by descending magnitude.</summary>
    public IReadOnlyList<ChampionPowerspikeEvent> Events { get; init; } = [];
}

/// <summary>
/// One point of the mean power curve. Internal to the read — it is the baseline
/// the spikes are measured against, and is not part of the response.
/// </summary>
public sealed record ChampionPowerCurvePoint
{
    public int Minute { get; init; }

    /// <summary>
    /// Opponent-relative power index at this minute: 0 = even with the lane
    /// opponent, positive = ahead. Unitless (σ-normalized blend of gold and
    /// damage lead), so the shape and the spikes carry the meaning, not the
    /// absolute value.
    /// </summary>
    public double Power { get; init; }

    /// <summary>Games contributing to this minute's average.</summary>
    public int Games { get; init; }
}

public sealed record ChampionPowerspikeEvent
{
    /// <summary><c>item</c> or <c>level</c>.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Item id for <c>item</c> events; champion level (6/11/16) for <c>level</c> events.</summary>
    public int RefId { get; init; }

    /// <summary>Mean minute the event occurs across games — where to anchor the marker on the curve.</summary>
    public double AvgMinute { get; init; }

    /// <summary>
    /// Mean change in the power-curve slope across a ±3 min window around the
    /// event (after-slope − before-slope), in excess of the ambient curvature the
    /// mean curve shows at that minute anyway. Positive = the champion's advantage
    /// accelerates after the event beyond the baseline — the power spike. The
    /// baseline subtraction removes the lead curve's global concavity, which would
    /// otherwise drive every event negative. Correlational, not causal.
    /// </summary>
    public double SpikeMagnitude { get; init; }

    /// <summary>Games the spike is averaged over.</summary>
    public int Games { get; init; }
}
