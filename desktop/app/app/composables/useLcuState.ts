import type { AppState, Screen } from '~/types/lcu'
import { EMPTY_STATE } from '~/types/lcu'

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

  onMounted(async () => {
    if (!insideTauri()) {
      if (import.meta.dev) {
        const { load, select } = useDevScenarios()
        await load()
        select(new URLSearchParams(window.location.search).get('scenario') ?? '')
      }
      ready.value = true
      return
    }

    const { invoke } = await import('@tauri-apps/api/core')
    const { listen } = await import('@tauri-apps/api/event')

    // Subscribe before the first read, or a change landing between the two
    // would be lost and the shell would sit on a stale state.
    const stop = await listen<AppState>('lcu://state', (event) => {
      state.value = event.payload
    })
    onScopeDispose(stop)

    state.value = await invoke<AppState>('current_state')
    ready.value = true
  })

  /**
   * Which screen to show. Mirrors `AppState::screen()` in Rust, which is the
   * authority — this exists so the frontend does not round-trip on every
   * render, not to hold a second copy of the rule.
   */
  const screen = computed<Screen>(() => {
    if (!state.value.connected) return 'no-client'
    switch (state.value.phase) {
      case 'ChampSelect': return 'draft'
      case 'InProgress': return 'in-game'
      default: return 'dashboard'
    }
  })

  return { state, screen, ready }
}
