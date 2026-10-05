import type { ChordView, OverlayPanel, OverlaySettings, OverlayView, PanelTrigger, TriggerCheck } from '~/types/overlay'
import { DEV_OVERLAY_VIEW, OVERLAY_PANELS, PANEL_KEY } from '~/types/overlay'
import { chordLabel, chordOfTrigger } from '~/utils/overlay-keys'

/** A browser-only `npm run dev`'s stand-in for the shell's chord labels: no game binds to warn about. */
function devChords(settings: OverlaySettings): OverlayView['chords'] {
  const chords: OverlayView['chords'] = {}
  for (const panel of OVERLAY_PANELS) {
    const chord = chordOfTrigger(settings[PANEL_KEY[panel]].trigger)
    if (chord) chords[panel] = { label: chordLabel(chord, true), warning: null }
  }
  return chords
}

/**
 * The in-game overlay's settings and preview, as the shell holds them
 * (`src-tauri/src/overlay`). Read by the settings panel in the app's window
 * and by each overlay panel's page, each in its own webview: the shell's
 * `overlay://view` event keeps them in step.
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
    if (command === 'set_overlay_settings') {
      const settings = args!.settings as OverlaySettings
      return { ...current, settings, chords: devChords(settings) }
    }
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

  /** Whether `trigger` can be `panel`'s, and how its chord reads, before it is saved. */
  async function check(panel: OverlayPanel, trigger: PanelTrigger): Promise<TriggerCheck> {
    if (insideTauri()) {
      const { invoke } = await import('@tauri-apps/api/core')
      return await invoke<TriggerCheck>('overlay_check_trigger', { panel, trigger })
    }
    const chord = chordOfTrigger(trigger)
    const view: ChordView | null = chord ? { label: chordLabel(chord, true), warning: null } : null
    return { refusal: null, chord: view }
  }

  /** Read the player's League keybindings, for the chords' warnings. */
  async function readBinds() {
    if (!insideTauri()) return
    view.value = await call('overlay_read_binds')
  }

  /** A panel page's measured size, for its window to take. */
  async function fit(panel: OverlayPanel, width: number, height: number) {
    if (!insideTauri()) return
    const { invoke } = await import('@tauri-apps/api/core')
    await invoke('overlay_fit', { panel, width, height })
  }

  return { view, save, preview, fit, check, readBinds }
}
