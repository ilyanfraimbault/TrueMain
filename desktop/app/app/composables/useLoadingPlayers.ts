import { IDLE_LOADING, type LoadingView } from '~/types/loading'
import { follow, shell, shellEvents } from '~/utils/shell'

/**
 * The loading screen's roster (#1753) as the shell reads it through the
 * player's own client: the ten players, then each one's form as its history
 * lands, and the board's status (`src-tauri/src/loading/`), followed from mount. In a browser-only `npm run dev`, the "In game ·
 * loading" scenario stands in (`utils/loading-dev.ts`).
 */
export function useLoadingPlayers() {
  const view = useState<LoadingView>('loading-players', () => IDLE_LOADING)
  const subscribed = useState<boolean>('loading-players-subscribed', () => false)

  if (!subscribed.value) {
    subscribed.value = true
    onMounted(async () => {
      if (insideTauri()) {
        onScopeDispose(await follow(shellEvents.loadingPlayers, shell.loadingPlayers, next => (view.value = next)))
      }
      else if (import.meta.dev) {
        const { devLoadingView } = await import('~/utils/loading-dev')
        view.value = devLoadingView()
      }
    })
  }

  return { view }
}
