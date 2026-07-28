// Mirrors backend/Api/ReadModels/Truemains/PlayerChampionPerformanceResponse.cs.
// The model behind the numbers is documented in docs/performance-score.md.

/**
 * The nine graded axes of the per-match performance score, in the backend's
 * own order (`Core.Lol.Performance.PerformanceComponentKind`).
 */
export const PERFORMANCE_COMPONENT_KINDS = [
  'Combat',
  'KillParticipation',
  'DamageShare',
  'GoldShare',
  'Farming',
  'Vision',
  'Laning',
  'MidGame',
  'Roam',
] as const

export type PerformanceComponentKind = typeof PERFORMANCE_COMPONENT_KINDS[number]

export interface PlayerChampionPerformanceComponent {
  kind: PerformanceComponentKind
  /** Nominal role weight on the 0..100 scale; 0 means the role does not grade it. */
  weight: number
  /** Mean 0..1 grade over the games the component was available in. Null when it never was. */
  value: number | null
  /** Games the component was available in — the denominator of `value`. */
  games: number
}

export interface PlayerChampionPerformanceResponse {
  championId: number
  /** Lane the sample was scoped to; null when every lane was counted. */
  position: string | null
  /** Major.minor patch the sample was scoped to; null for every patch. */
  patch: string | null
  /** Ranked solo/duo games actually graded. */
  games: number
  /** Sample floor: below this the averages are suppressed. */
  minGames: number
  /** Most recent games the panel ever looks at. */
  window: number
  /** Mean 0–100 score. Null below the floor. */
  averageScore: number | null
  bestScore: number | null
  worstScore: number | null
  /** Share of graded games the player topped their own side in (MVP or ACE), 0..1. Null below the floor. */
  topOfTeamRate: number | null
  /** Empty below the floor. */
  components: PlayerChampionPerformanceComponent[]
}
