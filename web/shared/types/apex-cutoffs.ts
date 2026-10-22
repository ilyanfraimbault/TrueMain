// The apex ladder thresholds of one region on one day: the lowest LP held by a
// Grandmaster and by a Challenger. Below Master a tier starts at a fixed LP;
// above it, these move every day, so the ranked card's LP curve needs them to
// draw where Grandmaster and Challenger begin.

export interface ApexCutoff {
  /** UTC calendar day of the measurement, `YYYY-MM-DD`. */
  dateUtc: string
  /** Lowest LP in the region's Grandmaster league that day, `null` when unmeasured. */
  grandmasterMinLp: number | null
  /** Lowest LP in the region's Challenger league that day, `null` when unmeasured. */
  challengerMinLp: number | null
}

/** `GET /ladder/{platformId}/apex-cutoffs` — mirrors `backend/Api/ReadModels/Ladder/ApexCutoffsReadModel.cs`. */
export interface ApexCutoffsResponse {
  /** One entry per measured UTC day, oldest first. */
  entries: ApexCutoff[]
}
