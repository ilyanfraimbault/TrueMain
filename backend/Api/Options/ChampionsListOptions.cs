namespace TrueMain.Options;

/// <summary>
/// Quality thresholds for the <c>GET /champions</c> directory. Product knobs the
/// user wants to tweak without a redeploy, so they bind from
/// <c>ChampionsList:*</c> in configuration.
/// </summary>
public sealed class ChampionsListOptions
{
    public const string SectionName = "ChampionsList";

    /// <summary>
    /// Minimum games a <c>(champion, lane)</c> line needs before it earns a tier
    /// and a spot in the list. Without a floor, one-game off-role picks (a single
    /// 1-0 = 100% win rate) top the patch-wide tier percentiles as a fluke and
    /// crowd every genuinely-played champion into the middle, collapsing the whole
    /// S→D scale to A/B. Lines below this are dropped from the payload (and from
    /// the ranking). Ten is low enough to surface a modestly-played champion at
    /// the current data volume while still keeping single-game flukes out of the
    /// percentiles. Set to 0 to disable.
    /// </summary>
    public int MinSampleGames { get; set; } = 10;

    /// <summary>
    /// Most <c>(champion, lane)</c> lines one champion may contribute to the
    /// directory and the tier list, keeping its most-played lanes. Champions
    /// flex, so a game's ~170 champions produced up to 5 × N lines — measured on
    /// patch 16.15, 561 lines for 173 champions, of which the lines beyond each
    /// champion's top two carried 5.9% of the games. A list that prints Ahri
    /// five times is not a champion list; two is the honest ceiling because a
    /// champion has at most one main lane and one real secondary. Lines beyond
    /// the cap are dropped from the payload and from the tier percentiles, so a
    /// lane's peer group is the champions genuinely played there. Set to 0 to
    /// disable the cap.
    /// </summary>
    public int MaxLanesPerChampion { get; set; } = 2;

    /// <summary>
    /// Share of a champion's own games a <em>secondary</em> lane must carry to
    /// count as one of its dominant positions — the read model's
    /// <c>LanePlayRate</c>. The cap alone is not enough: 37 of the 152 champions
    /// with a second lane on patch 16.15 played it in under 5% of their games —
    /// off-role picks, not a second identity — and printing them beside the real
    /// duals says the two are the same kind of fact.
    ///
    /// <para>
    /// A champion's most-played lane is always kept whatever its share, so a
    /// genuine five-lane flex still appears once rather than vanishing from a
    /// list of champions. Set to 0 to keep every lane up to the cap.
    /// </para>
    /// </summary>
    public double MinSecondaryLanePlayRate { get; set; } = 0.10;

    /// <summary>
    /// Absolute minimum games a champion-vs-opponent lane matchup needs before the
    /// matchup leaderboard includes it. A handful of games against a specific
    /// opponent is noise — a single lucky game would read as a 100% matchup — so
    /// opponents below this floor are dropped; the endpoint still returns 200 with
    /// the qualifying entries (an empty list when none clear the floor). Ten is the
    /// smallest sample where the head-to-head win rate starts to carry signal
    /// rather than echoing one or two games. Set to 0 to disable the floor.
    ///
    /// <para>
    /// This is the floor for *thin* champions only. On a heavily played one it is
    /// far too permissive — measured on production, Viego JUNGLE met 71 opponents
    /// over 14.7k games on a single patch and the 8 lines under 20 games were 0.2%
    /// of that sample yet held the entire best/worst leaderboard. The share-based
    /// <see cref="MinMatchupPlayRate"/> is what scales with volume; the effective
    /// floor is the larger of the two.
    /// </para>
    /// </summary>
    public int MinMatchupGames { get; set; } = 10;

    /// <summary>
    /// Minimum share of the champion's total matchup games — the games summed over
    /// every opponent it met in the same scope — a single opponent must hold to
    /// appear on the matchup leaderboard. The floor a matchup is actually judged
    /// against is <c>max(MinMatchupGames, MinMatchupPlayRate × total)</c>, so it
    /// tracks how much the champion is played instead of being tuned per champion.
    ///
    /// <para>
    /// 0.5% measured on production: it keeps 51 of Viego JUNGLE's 71 opponents on
    /// patch 16.15 (94.6% of the games) while cutting every line the leaderboard
    /// was previously topped by. On a thin champion it is *below* the absolute
    /// floor and therefore inert — Aurelion Sol MIDDLE's 1 650 games put it at 8,
    /// under <see cref="MinMatchupGames"/> — which is the point: no champion ends
    /// up with an empty panel because it is unpopular.
    /// </para>
    ///
    /// <para>
    /// The single-opponent search ignores this floor, like it ignores the absolute
    /// one: a deliberate lookup answers with whatever games exist. Set to 0 to
    /// disable.
    /// </para>
    /// </summary>
    public double MinMatchupPlayRate { get; set; } = 0.005;

    /// <summary>
    /// Minimum *decided* lanes (won or lost past the gold threshold at 15 minutes)
    /// behind the lane win rate before the matchup endpoints report it. Below this,
    /// the entry keeps its games and its game win rate and returns a null lane rate,
    /// which the frontend renders as an em dash.
    ///
    /// <para>
    /// A separate floor because it is a separate sample: the games floors above
    /// count games played, and only ~58% of those (production median) are ever
    /// decided lanes. Floor-clearing rows were printing "100% lane" off seven
    /// decided lanes — the most confident-looking cell on the panel resting on its
    /// smallest sample. Set to 0 to report every non-empty lane sample.
    /// </para>
    /// </summary>
    public int MinDecidedLaneGames { get; set; } = 10;

    /// <summary>
    /// Minimum games a champion-vs-opponent matchup needs in a
    /// <em>player-scoped</em> slice (one truemain's games) before the leaderboard
    /// includes it. A single player rarely meets the same lane opponent ten
    /// times, so reusing the global <see cref="MinMatchupGames"/> floor would
    /// empty almost every player's matchups list; this lower floor keeps the
    /// best/worst ranking meaningful without erasing it. The opponent search
    /// ignores both floors — a deliberate lookup shows the head-to-head from a
    /// single game up. Set to 0 to disable.
    /// </summary>
    public int MinPlayerMatchupGames { get; set; } = 3;

    /// <summary>
    /// Minimum games each side of the account-vs-mains comparison (#528) needs
    /// before the head-to-head is flagged comparable. Both columns are
    /// player-scoped slices of one champion, so the global
    /// <see cref="MinSampleGames"/> floor would empty almost every comparison;
    /// this sits just above the per-player matchup floor, where a win rate and a
    /// CS/min average stop echoing one or two games. The endpoint still returns
    /// both columns below the floor (with a status saying so) so the caller can
    /// show how far the sample is from the bar. Set to 0 to disable — a side
    /// with no games at all is never comparable regardless.
    /// </summary>
    public int MinComparisonGames { get; set; } = 5;

    /// <summary>
    /// Minimum games a (champion, partner) pairing needs before the synergies panel
    /// shows it. Set higher than <see cref="MinMatchupGames"/> on purpose: synergy is
    /// a <em>difference</em> between two rates, so its sampling error is the sum of
    /// theirs, and the honest floor for "this pairing is worth three points" is well
    /// above the floor for "this champion wins 54%". Ten games would let a 7-3 pair
    /// print +15% synergy and top the list. Set to 0 to disable.
    /// </summary>
    public int MinSynergyGames { get; set; } = 20;

    /// <summary>
    /// Minimum share of the champion's own games a pairing must appear in before the
    /// synergies panel shows it — the same shape as
    /// <see cref="MinMatchupPlayRate"/>, and for the same reason: an absolute floor
    /// alone lets the ranking fill up with pairings that happened a handful of times.
    /// The effective floor is <c>max(MinSynergyGames, MinSynergyPlayRate × the
    /// champion's games)</c>.
    ///
    /// <para>
    /// Set at 1%, twice the matchup floor, because synergy is a *difference* between
    /// two rates and so carries the sum of their sampling error — the same reasoning
    /// that already puts <see cref="MinSynergyGames"/> above
    /// <see cref="MinMatchupGames"/>. Measured on production, Viego JUNGLE's top four
    /// synergies were pairings of 21 to 26 games out of 8 202 (0.26%), led by a +24.7%
    /// "synergy" resting on 21 games; at 1% the list starts at Darius TOP over 271
    /// games and still holds 131 of 223 partners.
    /// </para>
    /// </summary>
    public double MinSynergyPlayRate { get; set; } = 0.01;

    /// <summary>
    /// Minimum share of a partner champion's own games — across every lane it is
    /// seen in as a teammate, in the same scope — that must sit on the lane a
    /// pairing is offered at. Below it the pairing is dropped whatever its volume:
    /// the lane is not a role that champion plays, so the pairing is a role-detection
    /// artefact rather than a duo anybody can pick.
    ///
    /// <para>
    /// This is <see cref="MinSecondaryLanePlayRate"/>'s idea (#1082) applied to the
    /// partner side, with its own denominator — hence its own option rather than a
    /// shared one. It is what removes lines like "Sylas BOTTOM", which topped Viego
    /// JUNGLE's synergies on production while Sylas is not an ADC. Deliberately a
    /// second filter and not a replacement for
    /// <see cref="MinSynergyPlayRate"/>: they catch different things, a pairing being
    /// rare and a pairing being impossible. Set to 0 to disable.
    /// </para>
    /// </summary>
    public double MinSynergyPartnerLanePlayRate { get; set; } = 0.10;

    /// <summary>
    /// Minimum games a trio (champion + chosen partner + third pick) needs before it
    /// is offered as a completion. Necessarily lower than
    /// <see cref="MinSynergyGames"/>: a trio's sample is a subset of its duo's, so
    /// reusing the pair floor would leave almost every duo with no third pick at all.
    /// It is the reason the endpoint returns the duo's own game count — a caller can
    /// then say "this duo has only 24 games, too few to split three ways" rather than
    /// implying no third pick works. Set to 0 to disable.
    /// </summary>
    public int MinSynergyTrioGames { get; set; } = 12;

    /// <summary>
    /// Minimum games a champion's marginal win rate must rest on before it may be
    /// used as an input to an expected win rate. This is the guard the other two
    /// floors cannot provide: a pairing can clear its own games floor while one side's
    /// baseline is still a coin flip, and since synergy is measured *against* that
    /// baseline, a noisy one produces a confidently wrong number rather than a noisy
    /// one. Entries whose partner baseline is below this are dropped, and a champion
    /// whose own baseline is below it yields an empty list. Set to 0 to disable.
    /// </summary>
    public int MinSynergyBaselineGames { get; set; } = 50;
}
