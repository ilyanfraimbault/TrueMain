import type { DraftRecommendation } from '~/types/draft'
import type { GameState } from '~/types/game'
import type { AppState } from '~/types/lcu'
import type { PlayerRecord } from '~/types/record'
import type { RankHistoryEntry } from '#shared/types/rank-history'
import { EMPTY_STATE } from '~/types/lcu'

export interface Scenario {
  id: string
  label: string
  state: AppState
  /** What the API would have answered for this draft, when the scenario shows the lane panel. */
  recommendation?: DraftRecommendation
  /** What the client's history would have answered, when the scenario shows the dashboard. */
  record?: PlayerRecord
  /** The Solo/Duo snapshots the app would have noted on this machine. */
  rankHistory?: RankHistoryEntry[]
  /**
   * The game Rust would have derived, when the scenario is in one. Checked
   * against the committed game tape by a test in `crates/live-client`, so it
   * cannot drift from what the feed actually sends.
   */
  game?: GameState
}

/**
 * Stand-ins for states the client would push, for `npm run dev` in a browser.
 *
 * Outside Tauri there is no client and no Rust, so every screen but "no client"
 * would be unreachable and the draft could only be looked at during a real
 * game. These make the UI reachable in a second, with hot reload.
 *
 * They prove nothing below the frontend: the parsing, the state derivation and
 * the navigation rule live in Rust and are exercised by a tape instead
 * (`lcu::tape`, and `TRUEMAIN_LCU_REPLAY` in the README). A scenario that
 * disagrees with what Rust would derive is a scenario that lies, so they are
 * written from `DraftState`, not invented.
 *
 * Dev-only by construction: every caller is behind `import.meta.dev`, which
 * lets the bundler drop the fixtures from a production build.
 */
export function useDevScenarios() {
  const scenarios = useState<Scenario[]>('dev-scenarios', () => [])
  const current = useState<string>('dev-scenario', () => '')
  const state = useState<AppState>('lcu-state', () => ({ ...EMPTY_STATE }))
  const recommendation = useState<DraftRecommendation | null>('dev-recommendation', () => null)
  const record = useState<PlayerRecord | null>('dev-record', () => null)
  const rankHistory = useState<RankHistoryEntry[]>('dev-rank-history', () => [])
  // `useLiveGame`'s own state, set directly as `lcu-state` is above.
  const game = useState<GameState | null>('live-game', () => null)
  const gameSyncedAt = useState<number>('live-game-synced-at', () => Date.now())

  async function load() {
    if (scenarios.value.length > 0) return
    const loaded = await import('~/fixtures/scenarios.json')
    scenarios.value = (loaded.default ?? loaded) as unknown as Scenario[]
  }

  /** Switch to a scenario. An unknown id falls back to the no-client state. */
  function select(id: string) {
    const found = scenarios.value.find(scenario => scenario.id === id)
    // A JSON round-trip rather than `structuredClone`: the scenarios come back
    // out of a reactive store as proxies, which `structuredClone` refuses. The
    // fixtures are plain JSON by construction, so this copy is faithful, and a
    // copy is needed at all so editing the state in place cannot corrupt the
    // scenario that is meant to be returned to.
    state.value = found ? JSON.parse(JSON.stringify(found.state)) : { ...EMPTY_STATE }
    recommendation.value = found?.recommendation ? JSON.parse(JSON.stringify(found.recommendation)) : null
    record.value = found?.record ? JSON.parse(JSON.stringify(found.record)) : null
    rankHistory.value = found?.rankHistory ? JSON.parse(JSON.stringify(found.rankHistory)) : []
    game.value = found?.game ? JSON.parse(JSON.stringify(found.game)) : null
    gameSyncedAt.value = Date.now()
    current.value = found?.id ?? ''

    // Kept in the URL so a scenario can be linked to, and so a reload after an
    // edit comes back to the screen being worked on rather than to the start.
    if (typeof window !== 'undefined') {
      const url = new URL(window.location.href)
      if (found) url.searchParams.set('scenario', found.id)
      else url.searchParams.delete('scenario')
      window.history.replaceState({}, '', url)
    }
  }

  return { scenarios, current, recommendation, record, rankHistory, load, select }
}
