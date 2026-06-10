namespace TrueMain.ReadModels.Ops;

/// <summary>
/// Per-champion corpus row for the admin champions panel. <see cref="Games"/> is
/// the raw participation count over <c>match_participants</c> (honouring the
/// region/patch/position/queue filters); the main/otp/extended-sample counts come
/// from <c>main_champion_stats</c> and are scoped by region only — patch, position
/// and queue do not apply to that table (it is computed per account-champion, not
/// per match). Champion names are intentionally omitted: the frontend resolves them
/// via DDragon.
/// </summary>
public sealed record ChampionStatRow
{
    public int ChampionId { get; init; }

    public long Games { get; init; }

    public int Mains { get; init; }

    public int Otps { get; init; }

    public int ExtendedSamples { get; init; }
}
