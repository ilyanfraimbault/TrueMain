namespace Core.Lol.Benchmarks;

/// <summary>
/// A whole-game figure the form benchmark (#2076) keeps a histogram of — the figures the
/// desktop dashboard's form tiles show, each read off one participant's end-of-game line
/// the way the tiles compute it (<c>utils/match-form.ts</c>).
/// </summary>
public enum FormMetric
{
    /// <summary><c>(kills + assists) / deaths</c>, deaths floored at one (<c>kdaRatio</c>).</summary>
    Kda = 0,

    /// <summary><c>(kills + assists) / team kills</c>, a fraction from 0 to 1; 0 when the team killed no one.</summary>
    KillParticipation = 1,

    /// <summary>Minions plus jungle monsters killed per minute of game.</summary>
    CsPerMinute = 2,

    /// <summary>Deaths in the game.</summary>
    Deaths = 3,

    /// <summary>Vision score per minute of game.</summary>
    VisionPerMinute = 4,
}
