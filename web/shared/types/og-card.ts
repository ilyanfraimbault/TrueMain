/**
 * Read-models for the dynamic social-share cards (#926).
 *
 * These are *not* mirrors of a backend read-model — they are assembled by the
 * `/api/og/**` handlers from the same public endpoints the pages use, and exist
 * so the Satori templates stay dumb (no fetching, no arithmetic, no defaults).
 *
 * The single rule the whole shape encodes: **every number on a card is a number
 * the API actually returned**. Anything we could not resolve is `null`, and the
 * template drops the block that would have carried it rather than printing a
 * placeholder — an OG image is read as a screenshot of the page, so a filler
 * `0%` would be indistinguishable from a measurement.
 */

export interface ChampionOgCard {
  championId: number
  /** DDragon display name; null when the static lookup failed (never invented from the id). */
  championName: string | null
  /** DDragon square portrait; null when the static lookup failed. */
  championIconUrl: string | null
  /**
   * The measured slice, or null when we hold no aggregate for this champion
   * (a champion nobody in the dataset mains, an upstream failure, or a rank
   * filter with no rows). Null degrades the card to its branded state — it
   * never falls back to another champion's or another lane's numbers.
   */
  stats: ChampionOgCardStats | null
}

export interface ChampionOgCardStats {
  /** Riot position the row was measured on — always labelled, never assumed. */
  position: string
  /** S/A/B/C/D, patch-relative, straight from the directory row. */
  tier: string
  winRate: number
  pickRate: number
  /** Null on patches predating ban ingestion (#920) — the chip is dropped, never zeroed. */
  banRate: number | null
  games: number
  patch: string
  /** Normalised elo filter the slice was measured under (`ALL`, `GOLD`, `GOLD_PLUS`, …). */
  eloBracket: string
}

export interface TruemainOgCard {
  /** `gameName#tagLine`; null when the profile is unknown or predates tag lines. */
  riotId: string | null
  /** Riot platform id (`EUW1`, `KR`, …); null when unknown. */
  platformId: string | null
  /**
   * Solo-queue standing, or null when the player has no ranked snapshot yet.
   * Null is rendered as "Unranked" — that is a real answer, unlike a 0 LP.
   */
  ranked: TruemainOgCardRanked | null
  /** The player's signature champion, or null when main analysis has classified none. */
  main: TruemainOgCardMain | null
  /** Dedication score (0–100) for the signature champion; null when there is none. */
  dedicationScore: number | null
}

export interface TruemainOgCardRanked {
  tier: string
  division: string
  leaguePoints: number
  /** Null when Riot's league response omitted it — the W-L line is dropped, not zeroed. */
  wins: number | null
  losses: number | null
  /** Only present when both wins and losses are. */
  winRate: number | null
}

export interface TruemainOgCardMain {
  championId: number
  championName: string | null
  championIconUrl: string | null
  games: number
  /** Share of the account's games on this champion (0..1). */
  playRate: number
  isOtp: boolean
}
