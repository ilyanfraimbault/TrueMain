import type { Update } from '@tauri-apps/plugin-updater'
import type { Screen } from '~/types/lcu'

/**
 * The app updates itself. Each build asks its own site's update feed
 * (`plugins.updater.endpoints`, set per flavour by `desktop-release.yml`)
 * whether a newer signed build exists — preprod's feed offers every version
 * bump merged to develop, production's only the promoted one — at launch and
 * then every fifteen minutes, since the companion stays open all day.
 *
 * A newer build is downloaded in the background, then:
 * - at launch, with no champion select or game running, it installs and
 *   restarts at once: the player has nothing open to lose yet;
 * - otherwise it waits for the player's click — in the toast, or in the
 *   sidebar for as long as the app runs — and installs at the next launch if
 *   they never click. A restart in the middle of champion select would cost
 *   them the draft, so the app never restarts under a running phase.
 *
 * A failed check stays silent: the app works on the version it has.
 */

const POLL_INTERVAL_MS = 15 * 60 * 1000
const BUSY_SCREENS: Screen[] = ['draft', 'in-game']

// The downloaded build waiting for a restart. Not reactive state: it is a
// handle on the shell's side, and the app has a single window for its life.
let downloaded: Update | null = null

export function useAppUpdate() {
  const toast = useToast()
  const { screen, ready: stateReady } = useLcuState()
  /** The version downloaded and waiting for a restart, for the sidebar. */
  const readyVersion = useState<string | null>('app-update-ready', () => null)
  const installing = useState<boolean>('app-update-installing', () => false)
  const started = useState<boolean>('app-update-started', () => false)
  let checking = false

  async function install(update: Update) {
    if (installing.value) return
    installing.value = true
    try {
      // Windows hands over to the installer here, which quits the app and
      // reopens it; macOS swaps the bundle in place and restarts below.
      await update.install()
      const { relaunch } = await import('@tauri-apps/plugin-process')
      await relaunch()
    }
    catch {
      installing.value = false
      toast.add({ title: 'The update could not be installed', description: 'It will be offered again at the next launch.', color: 'error', icon: 'i-lucide-circle-alert' })
    }
  }

  async function restart() {
    if (downloaded) await install(downloaded)
  }

  function offer(update: Update) {
    readyVersion.value = update.version
    toast.add({
      id: 'app-update',
      title: `TrueMain ${update.version} is ready`,
      description: 'Restart to update now, or it installs at the next launch. Your settings and favorites stay.',
      icon: 'i-lucide-download',
      duration: 0,
      actions: [{ label: 'Restart now', color: 'primary', onClick: () => void restart() }],
    })
  }

  async function check(atLaunch: boolean) {
    // Once a build waits for a restart, a newer one is picked up by the next launch's check.
    if (checking || installing.value || downloaded) return
    checking = true
    try {
      const { check: checkForUpdate } = await import('@tauri-apps/plugin-updater')
      const update = await checkForUpdate()
      if (!update) return
      await update.download()
      if (atLaunch && stateReady.value && !BUSY_SCREENS.includes(screen.value)) {
        toast.add({ id: 'app-update', title: `Updating TrueMain to ${update.version}`, description: 'The app restarts in a moment.', icon: 'i-lucide-download', duration: 0 })
        await install(update)
        return
      }
      downloaded = update
      offer(update)
    }
    catch {
      // Offline, the feed down, no release yet: the app runs as it is.
    }
    finally {
      checking = false
    }
  }

  /** Checks at launch, then on a timer for the window's life. Called once, by `app.vue`. */
  function start() {
    // `npm run dev` has no shell to update, and a dev build is not a release.
    if (started.value || !insideTauri() || import.meta.dev) return
    started.value = true
    void check(true)
    const timer = setInterval(() => void check(false), POLL_INTERVAL_MS)
    onScopeDispose(() => clearInterval(timer))
  }

  return { start, restart, readyVersion, installing }
}
