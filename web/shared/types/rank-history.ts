// Mirrors backend/Api/ReadModels/Truemains/RankHistoryReadModel.cs.
// See #302.

export interface RankHistoryResponse {
  entries: RankHistoryEntry[]
}

export interface RankHistoryEntry {
  /** ISO-8601 UTC timestamp at which the snapshot was captured. */
  capturedAtUtc: string
  /** Riot tier string (uppercase), e.g. `EMERALD`. */
  tier: string
  /** Riot roman division (`IV`, `III`, `II`, `I`). `"I"` for Master+. */
  division: string
  leaguePoints: number
}
