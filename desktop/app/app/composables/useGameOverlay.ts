import type { OverlayPanel, OverlaySettings, OverlayView, PanelTrigger, TriggerCheck } from '~/types/overlay'
import { follow, shell, shellEvents } from '~/utils/shell'

/**
 * The in-game overlay's settings and preview, as the shell holds them
 * (`src-tauri/src/overlay`). Read by the settings panel in the app's window
 * and by each overlay panel's page, each in its own webview: the shell's
 * event keeps them in step.
 */
export function useGameOverlay() {
  const view = useState<OverlayView | null>('overlay-view', () => null)
  const subscribed = useState<boolean>('overlay-subscribed', () => false)

  if (!subscribed.value) {
    subscribed.value = true
    onMounted(async () => {
      onScopeDispose(await follow(shellEvents.overlayView, shell.overlayView, next => (view.value = next)))
    })
  }

  /** Save one change on top of the current settings; the shell's answer is what then shows. */
  async function save(change: Partial<OverlaySettings>) {
    if (!view.value) return
    const settings = { ...view.value.settings, ...change }
    view.value = await shell.setOverlaySettings(settings)
  }

  async function preview(on: boolean) {
    if (!view.value || view.value.preview === on) return
    view.value = await shell.overlayPreview(on)
  }

  /** Whether `trigger` can be `panel`'s, and how its chord reads, before it is saved. */
  async function check(panel: OverlayPanel, trigger: PanelTrigger): Promise<TriggerCheck> {
    return await shell.overlayCheckTrigger(panel, trigger)
  }

  /** Read the player's League keybindings, for the chords' warnings. */
  async function readBinds() {
    view.value = await shell.overlayReadBinds()
  }

  /** A panel page's measured size, for its window to take. */
  async function fit(panel: OverlayPanel, width: number, height: number) {
    await shell.overlayFit(panel, width, height)
  }

  return { view, save, preview, fit, check, readBinds }
}
