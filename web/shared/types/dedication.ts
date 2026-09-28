// Mirrors backend/Api/ReadModels/Truemains/DedicationReadModel.cs.
// The formula lives in backend/Core/Truemains/DedicationScore.cs and is
// documented in docs/dedication-score.md. See #530, reworked in #1701.

/** Wire keys of a score part — mirrors `DedicationPartKeys` on the backend. */
export type TruemainScorePartKey = 'playRate' | 'mastery' | 'masteryRank'

/** One part of the score and what it contributed, in score points. */
export interface TruemainScorePart {
  key: TruemainScorePartKey
  /** Score points this part contributed (unrounded; the parts sum to the score). */
  points: number
  /** Score points this part can contribute at most. */
  maxPoints: number
}

/**
 * TrueMain's signature metric, shown as the **Truemain score**: how much of a
 * truemain a player is on one champion, on a 0..100 scale. The field keeps its
 * original `dedication` name on the wire (#1701). Ships the raw facts and each
 * part's contribution so the UI explains the number instead of asserting it —
 * never recompute the score on the client, the backend is the single source of
 * truth.
 */
export interface TruemainDedication {
  /** Final score, 0..100 (one decimal). */
  score: number
  /** The champion the score is about: the player's most-played main, or the filtered champion on the leaderboard. */
  championId: number
  /** Verdict: one-trick on the champion. Same flag as the OTP badge and the `otpOnly` filter. */
  isOtp: boolean
  /** Share of the player's recent ranked games spent on the champion (0..1). */
  playRate: number
  /** Recent ranked games on the champion — the numerator of `playRate`. */
  championGames: number
  /** Recent ranked games the play rate is measured over. */
  recentGames: number
  /** Riot champion-mastery points on the champion. Null until the mastery has been read. */
  masteryPoints: number | null
  /** 1-based rank of the champion in the player's mastery by points. Null until read, or when Riot has no entry. */
  masteryRank: number | null
  /** Whole days since the player last played the champion, per Riot mastery. Null until read. */
  daysSinceLastPlayed: number | null
  /** Each part's contribution, heaviest first. */
  parts: TruemainScorePart[]
}
