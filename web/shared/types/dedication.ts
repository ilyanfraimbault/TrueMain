// Mirrors backend/Api/ReadModels/Truemains/DedicationReadModel.cs.
// The formula lives in backend/Core/Truemains/DedicationScore.cs and is
// documented in docs/dedication-score.md. See #530.

/**
 * TrueMain's signature metric: how devoted a player is to one champion, on a
 * 0..100 scale. Ships with every component and raw input that produced it so
 * the UI can explain the number instead of asserting it — never recompute the
 * score on the client, the backend is the single source of truth.
 */
export interface TruemainDedication {
  /** Final score, 0..100 (one decimal). */
  score: number
  /** The champion the score is about: the player's most-played main, or the filtered champion on the leaderboard. */
  championId: number
  /** Share component (0..1): play rate on the champion, rescaled from the main-analysis play-rate floor. */
  commitment: number
  /** Time-span component (0..1): distinct tracked patches played on the champion. */
  span: number
  /** Sample-size component (0..1): career games on the champion, on a log curve. */
  volume: number
  /** Recency component (0..1): exponential decay on days since the last tracked game. */
  recency: number
  /** Raw share of the player's recent ranked games spent on the champion (0..1). */
  playRate: number
  /** Raw tracked ranked games on the champion, across every aggregated patch. */
  careerGames: number
  /** Raw count of distinct patches TrueMain has seen the player play the champion on. */
  patchSpan: number
  /** Whole days since the last tracked game on the champion. Null when no aggregated game exists yet. */
  daysSinceLastGame: number | null
}
