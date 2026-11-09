/** The slice a champion read asks for: every field optional, as the API reads them. */
export interface ChampionSliceQueryInput {
  patch?: string | null
  position?: string | null
  eloBracket?: string | null
  /** Mains only is the API's default, so only `false` travels. */
  truemainsOnly?: boolean | null
  opponentChampionId?: number | null
}

/**
 * The query string of a champion read, encoded one way for every caller: a field
 * goes on the wire only when it narrows the read, and the population only as
 * `truemainsOnly=false`. Pinning the defaults would make every resting request
 * carry redundant params and split one cache entry into two.
 */
export function championSliceQuery(slice: ChampionSliceQueryInput): Record<string, string | number> {
  const query: Record<string, string | number> = {}
  if (slice.patch) query.patch = slice.patch
  if (slice.position) query.position = slice.position
  if (slice.eloBracket) query.eloBracket = slice.eloBracket
  if (slice.truemainsOnly === false) query.truemainsOnly = 'false'
  if (slice.opponentChampionId) query.opponentChampionId = slice.opponentChampionId
  return query
}
