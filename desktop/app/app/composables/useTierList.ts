/** One `(champion, lane)` row of `GET /champions/tierlist`, with the tier it sits in. */
export interface TierEntry {
  championId: number
  position: string
  games: number
  winRate: number
  pickRate: number
  banRate: number
  tier: string
}

interface TierListResponse {
  patchVersion: string
  position: string | null
  tiers: { tier: string, entries: Omit<TierEntry, 'tier'>[] }[]
}

const TIER_ORDER = ['S', 'A', 'B', 'C', 'D']

/**
 * A failed load retries on its own, the wait doubling up to a ceiling. The
 * draft's pick panel is filtered through this list: one failure left untried
 * kept it empty for the whole champion select (#1783).
 */
const RETRY_FIRST_MS = 2_000
const RETRY_MAX_MS = 30_000
let retryTimer: ReturnType<typeof setTimeout> | undefined
let retryDelay = RETRY_FIRST_MS

/**
 * The site's tier list, every lane at once — the same S/A/B/C/D the site
 * computes server-side, never re-derived here. Fetched once per session —
 * retried until it lands: it moves with a patch, not with a draft, and every
 * pick card reads it.
 */
export function useTierList() {
  const entries = useState<TierEntry[]>('tierlist-entries', () => [])
  const patch = useState<string>('tierlist-patch', () => '')
  const status = useState<'idle' | 'pending' | 'ready' | 'error'>('tierlist-status', () => 'idle')

  async function load() {
    if (status.value === 'pending' || status.value === 'ready') return
    status.value = 'pending'
    try {
      const answer = await apiGet<TierListResponse>('/champions/tierlist')
      entries.value = answer.tiers.flatMap(group => group.entries.map(entry => ({ ...entry, tier: group.tier })))
      patch.value = answer.patchVersion
      status.value = 'ready'
      clearTimeout(retryTimer)
      retryTimer = undefined
      retryDelay = RETRY_FIRST_MS
    }
    catch {
      status.value = 'error'
      if (retryTimer === undefined) {
        retryTimer = setTimeout(() => {
          retryTimer = undefined
          void load()
        }, retryDelay)
        retryDelay = Math.min(retryDelay * 2, RETRY_MAX_MS)
      }
    }
  }
  void load()

  const byKey = computed(() => new Map(entries.value.map(entry => [`${entry.championId}:${entry.position}`, entry])))

  /** A champion on one lane; null when it is not played there enough to be tiered. */
  const entryOf = (championId: number | null, position: string | null | undefined): TierEntry | null => {
    if (championId === null || !position) return null
    return byKey.value.get(`${championId}:${position}`) ?? null
  }

  /** The lane a champion is played on most — for an enemy whose lane is not resolved yet. */
  const mainEntryOf = (championId: number | null): TierEntry | null => {
    if (championId === null) return null
    let best: TierEntry | null = null
    for (const entry of entries.value) {
      if (entry.championId === championId && (best === null || entry.games > best.games)) best = entry
    }
    return best
  }

  /** One lane, best tier first and the most played first within a tier. */
  const laneEntries = (position: string): TierEntry[] => entries.value
    .filter(entry => entry.position === position)
    .sort((a, b) => TIER_ORDER.indexOf(a.tier) - TIER_ORDER.indexOf(b.tier) || b.games - a.games)

  return { entries, patch, status, load, entryOf, mainEntryOf, laneEntries }
}
