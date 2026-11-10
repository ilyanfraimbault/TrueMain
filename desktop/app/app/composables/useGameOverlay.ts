import type { ChordView, OverlayPanel, OverlaySettings, OverlayView, PanelTrigger, TriggerCheck } from '~/types/overlay'
import { DEV_OVERLAY_VIEW, OVERLAY_PANELS, PANEL_KEY } from '~/types/overlay'
import { chordLabel, chordOfTrigger } from '~/utils/overlay-keys'
import { follow, shell, shellEvents } from '~/utils/shell'

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
 * event keeps them in step.
 *
 * In a browser-only `npm run dev` there is no shell: the defaults stand in,
 * changed in memory, so the settings panel can be worked on.
 */
export function useGameOverlay() {
  const view = useState<OverlayView | null>('overlay-view', () => null)
  const subscribed = useState<boolean>('overlay-subscribed', () => false)

  const current = () => view.value ?? DEV_OVERLAY_VIEW

  if (!subscribed.value) {
    subscribed.value = true
    onMounted(async () => {
      if (insideTauri()) onScopeDispose(await follow(shellEvents.overlayView, shell.overlayView, next => (view.value = next)))
      else view.value = current()
    })
  }

  /** Save one change on top of the current settings; the shell's answer is what then shows. */
  async function save(change: Partial<OverlaySettings>) {
    if (!view.value) return
    const settings = { ...view.value.settings, ...change }
    view.value = insideTauri()
      ? await shell.setOverlaySettings(settings)
      : { ...current(), settings, chords: devChords(settings) }
  }

  async function preview(on: boolean) {
    if (!view.value || view.value.preview === on) return
    view.value = insideTauri() ? await shell.overlayPreview(on) : { ...current(), preview: on }
  }

  /** Whether `trigger` can be `panel`'s, and how its chord reads, before it is saved. */
  async function check(panel: OverlayPanel, trigger: PanelTrigger): Promise<TriggerCheck> {
    if (insideTauri()) return await shell.overlayCheckTrigger(panel, trigger)
    const chord = chordOfTrigger(trigger)
    const view: ChordView | null = chord ? { label: chordLabel(chord, true), warning: null } : null
    return { refusal: null, chord: view }
  }

  /** Read the player's League keybindings, for the chords' warnings. */
  async function readBinds() {
    if (!insideTauri()) return
    view.value = await shell.overlayReadBinds()
  }

  /** A panel page's measured size, for its window to take. */
  async function fit(panel: OverlayPanel, width: number, height: number) {
    if (!insideTauri()) return
    await shell.overlayFit(panel, width, height)
  }

  return { view, save, preview, fit, check, readBinds }
}
