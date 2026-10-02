import type { OverlaySettings, OverlayView } from '~/types/overlay'
import { DEV_OVERLAY_VIEW } from '~/types/overlay'

/**
 * The in-game overlay's settings and preview, as the shell holds them
 * (`src-tauri/src/overlay`). Read by the settings panel in the app's window
 * and by the overlay's own page, each in its own webview: the shell's
 * `overlay://view` event keeps the two in step.
 *
 * In a browser-only `npm run dev` there is no shell: the defaults stand in,
 * changed in memory, so the settings panel can be worked on.
 */
export function useGameOverlay() {
  const view = useState<OverlayView | null>('overlay-view', () => null)
  const subscribed = useState<boolean>('overlay-subscribed', () => false)

  async function call(command: string, args?: Record<string, unknown>): Promise<OverlayView> {
    if (insideTauri()) {
      const { invoke } = await import('@tauri-apps/api/core')
      return await invoke<OverlayView>(command, args)
    }
    const current = view.value ?? DEV_OVERLAY_VIEW
    if (command === 'set_overlay_settings') return { ...current, settings: args!.settings as OverlaySettings }
    if (command === 'overlay_preview') return { ...current, preview: args!.on as boolean }
    return current
  }

  if (!subscribed.value) {
    subscribed.value = true
    onMounted(async () => {
      if (insideTauri()) {
        const { listen } = await import('@tauri-apps/api/event')
        const stop = await listen<OverlayView>('overlay://view', event => (view.value = event.payload))
        onScopeDispose(stop)
      }
      view.value = await call('overlay_view')
    })
  }

  /** Save one change on top of the current settings; the shell's answer is what then shows. */
  async function save(change: Partial<OverlaySettings>) {
    if (!view.value) return
    view.value = await call('set_overlay_settings', { settings: { ...view.value.settings, ...change } })
  }

  async function preview(on: boolean) {
    if (!view.value || view.value.preview === on) return
    view.value = await call('overlay_preview', { on })
  }

  /** The overlay page's measured size, for the panel to take. */
  async function fit(width: number, height: number) {
    if (!insideTauri()) return
    const { invoke } = await import('@tauri-apps/api/core')
    await invoke('overlay_fit', { width, height })
  }

  return { view, save, preview, fit }
}
