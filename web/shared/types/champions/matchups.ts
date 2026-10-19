// A champion's lane matchups, duo synergies and trio (third-pick) synergies.

/** One lane-matchup row: the champion's record against a single opponent. */
export interface ChampionMatchupEntry {
  opponentChampionId: number
  games: number
  wins: number
  winRate: number
  /**
   * Share of the champion's total matchup games this opponent holds, in the same
   * scope and before any floor. What the backend's leaderboard floor is expressed
   * in (`ChampionsList:MinMatchupPlayRate`), so it is also the honest way to say
   * how often the matchup actually happens — including on the single-opponent
   * search, which takes the denominator from its own scoped total. `0` only on the
   * player-scoped route, whose live join holds no such total.
   */
  playRate: number
  /**
   * Lower bound of the 95% Wilson interval around `winRate` — "at worst this
   * matchup is this good". **Rank the best list on this, not on `winRate`.**
   * Sorting the raw rate makes the leaderboard a small-sample detector: the most
   * extreme rate on a wide field is essentially always the thinnest sample, so
   * eleven games at 82% outranked 739 games at 57%. Both bound at ~52% and ~54%.
   */
  winRateLowerBound: number
  /**
   * Upper bound of the same interval, and the key for the *worst* list, ascending
   * — a bad matchup is one whose ceiling is low. Using the lower bound at both
   * ends would rank the worst list by which sample is thinnest, reintroducing at
   * the bottom exactly what the top was fixed for.
   */
  winRateUpperBound: number
  /**
   * Share of *decided* lanes won against this opponent — ahead by more than the
   * configured gold threshold at 15 minutes (#919). Denominator is
   * `decidedLaneGames`, never `games`: a match with no timeline, one that ended
   * before 15 min, or a lane inside the threshold band is not a decided lane.
   *
   * `null` = nothing can be said: fewer decided lanes than
   * `ChampionsList:MinDecidedLaneGames` (which includes none at all), or a
   * player-scoped slice, which has no lane data behind it. Never render it as 0%.
   */
  laneWinRate: number | null
  /** Lanes actually decided; the sample `laneWinRate` rests on. Smaller than `games`. */
  decidedLaneGames: number
  /**
   * Mean gold gap over the lane opponent at 15 minutes, signed from this champion's
   * side (#976) — the magnitude `laneWinRate` cannot carry. `null` when the gap was
   * never measured over this scope; rendering that as 0 would turn missing data into
   * the most decisive-looking verdict there is ("dead even").
   */
  averageGoldDiffAt15: number | null
  /**
   * Lanes `averageGoldDiffAt15` covers. Smaller than `decidedLaneGames` on matchups
   * folded before #976 — it is the sample a verdict may be banded from, and the reason
   * a thin one shows the count instead of a label.
   */
  goldDiffLaneGames: number
  /**
   * Mean experience gap at 15 min, signed from this champion's side (#1111). Not a
   * restatement of the gold gap: gold is who bought more, XP is who is bigger, and a
   * lane won on kills while losing waves shows a gold lead over an XP deficit. `null`
   * when never measured — never render it as 0.
   */
  averageXpDiffAt15: number | null
  /** Lanes `averageXpDiffAt15` covers; its own denominator, like the gold one. */
  xpDiffLaneGames: number
}

/**
 * All of a champion's lane matchups at a position and (when the caller pins one)
 * patch. Served from the `champion_matchup_stats` aggregate for every global
 * slice, search included; computed live from match participants only for the
 * player-scoped route, which the aggregate has no dimension for. The client
 * slices a best/worst leaderboard out of it and filters it for the search.
 */
export interface ChampionMatchups {
  championId: number
  position: string
  patch: string | null
  matchups: ChampionMatchupEntry[]
}

/**
 * One duo partner row. `synergy` is the value the list is ranked by — how far
 * `winRate` lands above (positive) or below what the two champions' own win
 * rates already predicted. A high `winRate` with a `synergy` near zero means
 * "this champion wins a lot", not "this pairing works".
 */
export interface ChampionSynergyEntry {
  partnerChampionId: number
  partnerPosition: string
  games: number
  wins: number
  winRate: number
  /**
   * Share of the champion's own games this partner was on the team for. The
   * quantity `ChampionsList:MinSynergyPlayRate` floors — a pairing can carry a
   * spectacular synergy and still be something that happened 21 times out of
   * 8 202 games.
   */
  playRate: number
  /** Sample behind `partnerBaselineWinRate`; always ≥ `games`. */
  partnerBaselineGames: number
  /** The partner's win rate as somebody's teammate, across all their pairings. */
  partnerBaselineWinRate: number
  expectedWinRate: number
  /** `winRate - expectedWinRate`, on the same 0–1 scale as a win rate. */
  synergy: number
}

/** `GET /api/champions/{id}/synergies` — the duo half of the synergies panel. */
export interface ChampionSynergies {
  championId: number
  position: string
  patch: string | null
  /** The partner lane the request narrowed to, or null for every lane. */
  partnerPosition: string | null
  /** Minimum shared games a pairing needed to appear — echoed to explain an empty list. */
  minGames: number
  /** Sample behind `championWinRate`. Zero means the champion has no games in scope. */
  championGames: number
  championWinRate: number
  /** The tracked cohort's overall win rate — the reference point partners are judged against. */
  cohortWinRate: number
  partners: ChampionSynergyEntry[]
}

/** One third-pick row for a chosen duo. */
export interface ChampionTrioSynergyEntry {
  championId: number
  position: string
  games: number
  wins: number
  winRate: number
  baselineGames: number
  baselineWinRate: number
  expectedWinRate: number
  synergy: number
}

/**
 * `GET /api/champions/{id}/synergies/trios` — computed live from the games the
 * chosen duo actually shared, so `pairGames` is the ceiling every completion's
 * sample is drawn from and an empty `completions` list is the normal answer for
 * a rarely-played duo.
 */
export interface ChampionTrioSynergies {
  championId: number
  position: string
  partnerChampionId: number
  partnerPosition: string
  patch: string | null
  minGames: number
  pairGames: number
  pairWins: number
  pairWinRate: number
  completions: ChampionTrioSynergyEntry[]
}
