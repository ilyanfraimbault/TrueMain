import { IDLE_LOADING, type LoadingView } from '~/types/loading'
import { follow, shell, shellEvents } from '~/utils/shell'

/**
 * The loading screen's roster (#1753) as the shell reads it through the
 * player's own client: the ten players, then each one's form as its history
 * lands, and the board's status (`src-tauri/src/loading/`), followed from mount.
 */
export function useLoadingPlayers() {
  const view = useState<LoadingView>('loading-players', () => IDLE_LOADING)
  const subscribed = useState<boolean>('loading-players-subscribed', () => false)

  if (!subscribed.value) {
    subscribed.value = true
    onMounted(async () => {
      onScopeDispose(await follow(shellEvents.loadingPlayers, shell.loadingPlayers, next => (view.value = next)))
    })
  }

  return { view }
}
