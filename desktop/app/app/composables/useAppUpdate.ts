import type { Update } from '@tauri-apps/plugin-updater'
import type { Screen } from '~/types/lcu'

/**
 * The app updates itself. Each build asks its own site's update feed
 * (`plugins.updater.endpoints`, set per flavour by `desktop-release.yml`)
 * whether a newer signed build exists — preprod's feed offers a build of every
 * app change merged to develop, production's a version bump once production
 * runs what it reads — at launch and then every fifteen minutes, since the
 * companion stays open all day.
 *
 * A newer build is downloaded in the background, then:
 * - at launch, with no champion select or game running, it installs and
 *   restarts at once: the player has nothing open to lose yet;
 * - otherwise it waits for the player's click — in the toast, or in the
 *   sidebar for as long as the app runs — and installs at the next launch if
 *   they never click. A restart in the middle of champion select would cost
 *   them the draft, so the app never restarts under a running phase.
 *
 * An install that has not restarted the app after `INSTALL_DEADLINE_MS` —
 * seen once on macOS, the bundle untouched and the process still running
 * (#1793) — drops the progress toast for a "Restart now". The shell's install
 * cannot be cancelled from here, so the stalled one is never retried in this
 * run: the restart either opens the new build or the old one, whose launch
 * check offers the update again.
 *
 * "Check for Updates…" — in the app menu on macOS, the tray icon's menu on
 * Windows (`src-tauri/src/menu.rs`) — runs the same check on demand and says
 * how it went. Otherwise a failed check stays silent: the app works on the
 * version it has.
 */

const POLL_INTERVAL_MS = 15 * 60 * 1000
// How long the launch check waits for the shell's first state before treating the phase as unknown.
const STATE_WAIT_MS = 10 * 1000
const BUSY_SCREENS: Screen[] = ['draft', 'in-game']
// A healthy install restarts the app in about ten seconds.
export const INSTALL_DEADLINE_MS = 30 * 1000

// The downloaded build waiting for a restart. Not reactive state: it is a
// handle on the shell's side, and the app has a single window for its life.
let downloaded: Update | null = null

export function useAppUpdate() {
  const toast = useToast()
  const { screen, ready: stateReady } = useLcuState()
  /** The version downloaded and waiting for a restart, for the sidebar. */
  const readyVersion = useState<string | null>('app-update-ready', () => null)
  const installing = useState<boolean>('app-update-installing', () => false)
  /** An install past its deadline: the app offers a plain restart instead of waiting on it. */
  const stalled = useState<boolean>('app-update-stalled', () => false)
  const started = useState<boolean>('app-update-started', () => false)
  let checking = false

  async function install(update: Update) {
    if (installing.value) return
    installing.value = true
    // Not cleared on success: a relaunch that returns has not restarted anything.
    const deadline = setTimeout(() => stall(update.version), INSTALL_DEADLINE_MS)
    try {
      // Windows hands over to the installer here, which quits the app and
      // reopens it; macOS swaps the bundle in place and restarts below.
      await update.install()
      const { relaunch } = await import('@tauri-apps/plugin-process')
      await relaunch()
    }
    catch {
      clearTimeout(deadline)
      stalled.value = false
      installing.value = false
      toast.remove('app-update')
      toast.add({ title: 'The update could not be installed', description: 'It will be offered again at the next launch.', color: 'error', icon: 'i-lucide-circle-alert' })
    }
  }

  /** Whether champion select or a game is known not to be running; unknown counts as busy. */
  async function idle() {
    if (!stateReady.value) {
      await new Promise<void>((resolve) => {
        const timeout = setTimeout(done, STATE_WAIT_MS)
        const stop = watch(stateReady, value => value && done())
        function done() {
          clearTimeout(timeout)
          stop()
          resolve()
        }
      })
    }
    return stateReady.value && !BUSY_SCREENS.includes(screen.value)
  }

  /**
   * The install is still running, or hung, past its deadline. `installing`
   * stays set: a second install over the first one could leave half a bundle.
   */
  function stall(version: string) {
    stalled.value = true
    say({
      title: `Updating TrueMain to ${version} is taking longer than expected`,
      description: 'Restart now to finish. If the update was not applied, it is offered again at the next launch.',
      icon: 'i-lucide-hourglass',
      actions: [{ label: 'Restart now', onClick: () => void relaunchNow() }],
    })
  }

  async function relaunchNow() {
    try {
      const { relaunch } = await import('@tauri-apps/plugin-process')
      await relaunch()
    }
    catch {
      say({ title: 'TrueMain could not restart', description: 'Quit and reopen the app to finish the update.', icon: 'i-lucide-circle-alert', color: 'error' })
    }
  }

  async function restart() {
    if (stalled.value) await relaunchNow()
    else if (downloaded) await install(downloaded)
  }

  /**
   * The one update toast, rewritten in place as a check moves on. Every field is
   * set each time: a toast added under an existing id is merged into it, so an
   * omitted field would keep the previous message's.
   */
  function say(message: { title: string, description?: string, icon: string, color?: 'primary' | 'error' | 'success', duration?: number, actions?: { label: string, onClick: () => void }[] }) {
    toast.add({
      id: 'app-update',
      description: undefined,
      color: 'primary',
      duration: 0,
      actions: undefined,
      ...message,
    })
  }

  function offer(update: Update) {
    readyVersion.value = update.version
    say({
      title: `TrueMain ${update.version} is ready`,
      description: 'Restart to update now, or it installs at the next launch. Your settings and favorites stay.',
      icon: 'i-lucide-download',
      actions: [{ label: 'Restart now', onClick: () => void restart() }],
    })
  }

  /**
   * One look at the feed. `launch` may install at once, `timer` stays silent
   * unless it finds a build, and `manual` — "Check for Updates…" in the macOS
   * app menu or the Windows tray menu — answers every outcome, up to date and
   * offline included.
   */
  async function check(trigger: 'launch' | 'timer' | 'manual') {
    const manual = trigger === 'manual'
    if (installing.value) return
    // Once a build waits for a restart, a newer one is picked up by the next launch's check.
    if (downloaded) {
      if (manual) offer(downloaded)
      return
    }
    if (checking) return
    checking = true
    if (manual) say({ title: 'Checking for updates…', icon: 'i-lucide-refresh-cw' })
    try {
      const { check: checkForUpdate } = await import('@tauri-apps/plugin-updater')
      const update = await checkForUpdate()
      if (!update) {
        if (manual) {
          const { getVersion } = await import('@tauri-apps/api/app')
          say({ title: 'TrueMain is up to date', description: `You have the latest version, ${await getVersion()}.`, icon: 'i-lucide-circle-check', color: 'success', duration: 5000 })
        }
        return
      }
      if (manual) say({ title: `Downloading TrueMain ${update.version}…`, icon: 'i-lucide-download' })
      await update.download()
      if (trigger === 'launch' && await idle()) {
        say({ title: `Updating TrueMain to ${update.version}`, description: 'The app restarts in a moment.', icon: 'i-lucide-download' })
        await install(update)
        return
      }
      downloaded = update
      offer(update)
    }
    catch {
      // Offline, the feed down, no release yet: the app runs as it is — and says so only when asked.
      if (manual) say({ title: 'Could not check for updates', description: 'Check your connection and try again.', icon: 'i-lucide-circle-alert', color: 'error', duration: 5000 })
    }
    finally {
      checking = false
    }
  }

  /**
   * Checks at launch, then on a timer for the window's life, and whenever the
   * shell's "Check for Updates…" menu item is chosen (`menu.rs`). Called once,
   * by `app.vue`.
   */
  async function start() {
    // `npm run dev` has no shell to update, and a dev build is not a release.
    if (started.value || !insideTauri() || import.meta.dev) return
    started.value = true
    const timer = setInterval(() => void check('timer'), POLL_INTERVAL_MS)
    // Registered before the first await, which would leave the component's scope behind.
    let stopListening: (() => void) | undefined
    onScopeDispose(() => {
      clearInterval(timer)
      stopListening?.()
    })
    void check('launch')
    const { listen } = await import('@tauri-apps/api/event')
    stopListening = await listen('app://check-for-updates', () => void check('manual'))
  }

  return { start, restart, readyVersion, installing, stalled }
}
