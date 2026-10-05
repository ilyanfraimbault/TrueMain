import type { LoadingView } from '~/types/loading'

/**
 * The loading screen's roster (#1753) as the shell reads it through the
 * player's own client: the ten players, then each one's form as its history
 * lands (`src-tauri/src/loading.rs`). Read on mount, then followed through
 * `loading://players`. In a browser-only `npm run dev`, the "In game ·
 * loading" scenario stands in (`utils/loading-dev.ts`).
 */
export function useLoadingPlayers() {
  const view = useState<LoadingView>('loading-players', () => ({ players: [], platformId: '' }))
  const subscribed = useState<boolean>('loading-players-subscribed', () => false)

  if (!subscribed.value) {
    subscribed.value = true
    onMounted(async () => {
      if (insideTauri()) {
        const { invoke } = await import('@tauri-apps/api/core')
        const { listen } = await import('@tauri-apps/api/event')
        const stop = await listen<LoadingView>('loading://players', event => (view.value = event.payload))
        onScopeDispose(stop)
        view.value = await invoke<LoadingView>('loading_players')
      }
      else if (import.meta.dev) {
        const { devLoadingView } = await import('~/utils/loading-dev')
        view.value = devLoadingView()
      }
    })
  }

  return { view }
}
