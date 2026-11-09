import { isChampSelect } from '~/utils/gameflow'
import type { AppState, Screen } from '~/types/lcu'
import { EMPTY_STATE } from '~/types/lcu'
import { follow, shell, shellEvents } from '~/utils/shell'

/**
 * Whether the shell is hosting us, rather than a plain browser.
 *
 * The test is on `__TAURI_INTERNALS__` rather than a try/catch around the
 * first call, so a genuine failure inside Tauri still surfaces instead of
 * looking like "not running under Tauri".
 */
export function insideTauri() {
  return typeof window !== 'undefined' && '__TAURI_INTERNALS__' in window
}

/**
 * The app's state, pushed from Rust.
 *
 * Runs in a plain browser too: `npm run dev` outside Tauri opens on a dev
 * scenario, which is how the UI is worked on without League installed.
 */
export function useLcuState() {
  const state = useState<AppState>('lcu-state', () => ({ ...EMPTY_STATE }))
  const ready = useState<boolean>('lcu-ready', () => false)
  // Every component that reads the state calls this; only the first one —
  // `app.vue`, which lives as long as the window — subscribes.
  const subscribed = useState<boolean>('lcu-subscribed', () => false)

  if (!subscribed.value) {
    subscribed.value = true
    onMounted(subscribe)
  }

  async function subscribe() {
    if (!insideTauri()) {
      if (import.meta.dev) {
        const { load, select } = useDevScenarios()
        await load()
        select(new URLSearchParams(window.location.search).get('scenario') ?? '')
      }
      ready.value = true
      return
    }

    onScopeDispose(await follow(shellEvents.lcuState, shell.currentState, next => (state.value = next)))
    ready.value = true
  }

  /**
   * Which screen to show. Mirrors `AppState::screen()` in Rust, which is the
   * authority — this exists so the frontend does not round-trip on every
   * render, not to hold a second copy of the rule.
   */
  const screen = computed<Screen>(() => {
    if (!state.value.connected) return 'no-client'
    if (isChampSelect(state.value.phase)) return 'draft'
    // Not `isGameRunning`: during a reconnect the game's API is down.
    if (state.value.phase === 'InProgress') return 'in-game'
    return 'dashboard'
  })

  return { state, screen, ready }
}
