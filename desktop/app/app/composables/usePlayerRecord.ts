import type { RankHistoryEntry } from '~~/shared/types/rank-history'
import type { PlayerGame, PlayerRecord } from '~/types/record'
import { readRankHistory, recordRankSnapshot } from '~/utils/lp-history'

/** A record younger than this is shown as is when the dashboard opens again. */
const FRESH_FOR_MS = 120_000

/** The client's page of history, as `player_record` and `player_history` read it. */
const HISTORY_PAGE = 20

/** How far back the match history pages. The client keeps a bounded history anyway. */
const HISTORY_CEILING = 100

/**
 * The logged-in player's own record — standing, profile background, latest
 * games — read from their client by the shell (`player_record`). It works for
 * any player, tracked by TrueMain or not.
 *
 * Held for the session and tied to the Riot ID it was read for, so a relog
 * never shows the previous account's games. Each read also notes the Solo/Duo
 * standing in the player's LP history on this machine (`utils/lp-history`). In
 * `npm run dev` outside Tauri the dev scenario stands in for both.
 *
 * The record is the latest page of games; `loadOlder` reads the pages before
 * it as the match history is paged back.
 */
export function usePlayerRecord() {
  const { state } = useLcuState()
  const record = useState<PlayerRecord | null>('player-record', () => null)
  const owner = useState<string | null>('player-record-owner', () => null)
  const readAt = useState<number>('player-record-read-at', () => 0)
  const status = useState<'idle' | 'pending' | 'ready' | 'error'>('player-record-status', () => 'idle')
  const rankHistory = useState<RankHistoryEntry[]>('player-rank-history', () => [])
  const older = useState<PlayerGame[]>('player-record-older', () => [])
  const hasOlder = useState<boolean>('player-record-has-older', () => false)
  const loadingOlder = useState<boolean>('player-record-loading-older', () => false)

  async function read(): Promise<PlayerRecord | null> {
    if (insideTauri()) {
      const { invoke } = await import('@tauri-apps/api/core')
      return await invoke<PlayerRecord>('player_record')
    }
    if (import.meta.dev) return useDevScenarios().record.value
    return null
  }

  /** Read again, unless a fresh record for this player is already held (`force` reads anyway). */
  async function refresh(force = false) {
    const riotId = state.value.riotId
    if (!state.value.connected || !riotId) return
    const fresh = owner.value === riotId && Date.now() - readAt.value < FRESH_FOR_MS
    if (status.value === 'pending' || (fresh && !force)) return

    if (owner.value !== riotId) record.value = null
    status.value = 'pending'
    try {
      const answer = await read()
      // The player switched accounts while this was out: read theirs instead.
      if (state.value.riotId !== riotId) {
        status.value = 'idle'
        return void refresh()
      }
      record.value = answer
      // A new game shifts every older page by one: they are read again from here.
      older.value = []
      hasOlder.value = (answer?.games.length ?? 0) >= HISTORY_PAGE - 1
      rankHistory.value = noteStanding(riotId, answer)
      owner.value = riotId
      readAt.value = Date.now()
      status.value = 'ready'
    }
    catch {
      status.value = 'error'
    }
  }

  function noteStanding(riotId: string, answer: PlayerRecord | null): RankHistoryEntry[] {
    if (!insideTauri()) return import.meta.dev ? useDevScenarios().rankHistory.value : []
    const solo = answer?.ranked.find(queue => queue.queueType === 'RANKED_SOLO_5x5') ?? null
    return answer ? recordRankSnapshot(riotId, solo) : readRankHistory(riotId)
  }

  /** Every game read so far, newest first — the record, then the older pages. */
  const games = computed<PlayerGame[]>(() => {
    if (owner.value !== state.value.riotId || !record.value) return []
    const seen = new Set(record.value.games.map(game => game.gameId))
    return [...record.value.games, ...older.value.filter(game => !seen.has(game.gameId))]
  })

  /** Read the next older page; an empty one means the client has no more. */
  async function loadOlder() {
    const riotId = state.value.riotId
    if (loadingOlder.value || !hasOlder.value || !riotId) return
    loadingOlder.value = true
    try {
      const begin = games.value.length
      const page = await readOlder(begin)
      if (state.value.riotId !== riotId) return
      const known = new Set(games.value.map(game => game.gameId))
      older.value = [...older.value, ...page.filter(game => !known.has(game.gameId))]
      hasOlder.value = page.length > 0 && begin + page.length < HISTORY_CEILING
    }
    catch {
      hasOlder.value = false
    }
    finally {
      loadingOlder.value = false
    }
  }

  async function readOlder(begin: number): Promise<PlayerGame[]> {
    if (!insideTauri()) return []
    const { invoke } = await import('@tauri-apps/api/core')
    return await invoke<PlayerGame[]>('player_history', { begin })
  }

  const current = computed(() => (owner.value === state.value.riotId ? record.value : null))
  const history = computed(() => (owner.value === state.value.riotId ? rankHistory.value : []))

  return { record: current, games, rankHistory: history, status, refresh, hasOlder, loadingOlder, loadOlder }
}
