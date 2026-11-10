import type { HudSettings, OverlayPanel, OverlayView } from '~/generated/shell'
import { DEFAULT_HUD as SHELL_DEFAULT_HUD, DEFAULT_OVERLAY_SETTINGS, OVERLAY_LIMITS, OVERLAY_PANEL_TABLE } from '~/generated/shell'
import { writable } from '~/utils/writable'

/** The overlay's settings and views as the shell keeps them (`crates/shell-state`), generated from Rust. */
export type {
  Chord,
  ChordView,
  HudSettings,
  OverlayAnchor,
  OverlayPanel,
  OverlaySettings,
  OverlayShow,
  OverlayView,
  PanelSettings as OverlayPanelSettings,
  PanelTrigger,
  TriggerCheck,
} from '~/generated/shell'

/** The order the Overlay page lists the panels in. */
export const OVERLAY_PANELS = ['next-item', 'win-probability', 'stats', 'objectives', 'item-value', 'lane-gold'] as const satisfies readonly OverlayPanel[]
// Every panel the shell has is listed above, or this fails to compile.
const _listed: Exclude<OverlayPanel, typeof OVERLAY_PANELS[number]> extends never ? true : never = true

/** Placed on the game's scoreboard, never dragged. */
export const onScoreboard = (panel: OverlayPanel) => OVERLAY_PANEL_TABLE[panel].onScoreboard

/** The settings key of each panel. */
export const PANEL_KEY = Object.fromEntries(
  Object.entries(OVERLAY_PANEL_TABLE).map(([panel, { field }]) => [panel, field]),
) as { [P in OverlayPanel]: (typeof OVERLAY_PANEL_TABLE)[P]['field'] }

/** What each panel shows, for the player choosing them; its name is the shell's. */
const DESCRIPTIONS: Record<OverlayPanel, string> = {
  'next-item': 'The next item, the gold it still needs, and its components in the order the mains buy them.',
  'win-probability': 'Each side\'s chance to win, from the item-gold gap and the map. The whole game.',
  'stats': 'CS and gold per minute, beside the median of games at your Solo/Duo tier. Gold reads a little low: used potions and sell-back losses are invisible to the game.',
  'item-value': 'What each team\'s items are worth, and each lane\'s gap. While TAB is held by default.',
  'objectives': 'When the dragon, the Voidgrubs, the Rift Herald and Baron are next up, and the drakes each team holds. From the game clock and the kills the game announces.',
  'lane-gold': 'Each lane\'s item-gold gap on a plate down the middle of the game\'s scoreboard, on its row. While TAB is held by default.',
}

export const OVERLAY_PANEL_INFO = Object.fromEntries(
  OVERLAY_PANELS.map(panel => [panel, { label: OVERLAY_PANEL_TABLE[panel].name, description: DESCRIPTIONS[panel] }]),
) as Record<OverlayPanel, { label: string, description: string }>

export const OVERLAY_SCALE = OVERLAY_LIMITS.scale
export const OVERLAY_OPACITY = OVERLAY_LIMITS.opacity
export const SCOREBOARD_NUDGE = OVERLAY_LIMITS.scoreboardNudge
/** A new League install's HUD, until the client is read. */
export const DEFAULT_HUD: HudSettings = { ...SHELL_DEFAULT_HUD }

/** What a browser-only `npm run dev` shows: the shell's defaults. */
export const DEV_OVERLAY_VIEW: OverlayView = {
  settings: writable(DEFAULT_OVERLAY_SETTINGS),
  supported: true,
  preview: false,
  shortcut: '⌥⇧O',
  notice: null,
  chords: { 'item-value': { label: 'Tab', warning: null }, 'lane-gold': { label: 'Tab', warning: null } },
  ownBinds: false,
  hud: DEFAULT_HUD,
}
