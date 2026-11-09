import type { HudSettings, OverlayPanel, OverlaySettings, OverlayView } from '~/generated/shell'

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

export const OVERLAY_PANELS: OverlayPanel[] = ['next-item', 'win-probability', 'stats', 'objectives', 'item-value', 'lane-gold']

/** Mirrors `OverlayPanel::on_scoreboard`: placed on the game's scoreboard, never dragged. */
export const onScoreboard = (panel: OverlayPanel) => panel === 'lane-gold'

/** What each panel is called and shows, for the player choosing them. */
export const OVERLAY_PANEL_INFO: Record<OverlayPanel, { label: string, description: string }> = {
  'next-item': { label: 'Build path', description: 'The next item, the gold it still needs, and its components in the order the mains buy them.' },
  'win-probability': { label: 'Win probability', description: 'Each side\'s chance to win, from the item-gold gap and the map. The whole game.' },
  'stats': { label: 'Your pace', description: 'CS and gold per minute, beside the median of games at your Solo/Duo tier. Gold reads a little low: used potions and sell-back losses are invisible to the game.' },
  'item-value': { label: 'Item value', description: 'What each team\'s items are worth, and each lane\'s gap. While TAB is held by default.' },
  'objectives': { label: 'Objectives', description: 'When the dragon, the Voidgrubs, the Rift Herald and Baron are next up, and the drakes each team holds. From the game clock and the kills the game announces.' },
  'lane-gold': { label: 'Lane gold', description: 'Each lane\'s item-gold gap on a plate down the middle of the game\'s scoreboard, on its row. While TAB is held by default.' },
}

/** The settings key of each panel. */
export const PANEL_KEY = {
  'next-item': 'nextItem',
  'win-probability': 'winProbability',
  'item-value': 'itemValue',
  'stats': 'stats',
  'objectives': 'objectives',
  'lane-gold': 'laneGold',
} as const satisfies Record<OverlayPanel, keyof OverlaySettings>

export const OVERLAY_SCALE = { min: 0.8, max: 1.4 } as const
export const OVERLAY_OPACITY = { min: 0.5, max: 1 } as const
/** Mirrors `scoreboard::MAX_NUDGE`. */
export const SCOREBOARD_NUDGE = 60
/** Mirrors `HudSettings::DEFAULTS`: a new League install's HUD. */
export const DEFAULT_HUD: HudSettings = { globalScale: 0, mirrored: false, own: false }

const TAB_HELD = { kind: 'whileHeld', chord: { alt: false, shift: false, ctrl: false, meta: false, tab: true, key: null } } as const

/** What a browser-only `npm run dev` shows: the shell's defaults. */
export const DEV_OVERLAY_VIEW: OverlayView = {
  settings: {
    enabled: true,
    show: 'always',
    scale: 1,
    opacity: 0.95,
    nextItem: { enabled: true, anchor: 'top-right', custom: null, trigger: { kind: 'always' } },
    winProbability: { enabled: true, anchor: 'top-left', custom: null, trigger: { kind: 'always' } },
    itemValue: { enabled: true, anchor: 'center-right', custom: null, trigger: TAB_HELD },
    stats: { enabled: true, anchor: 'center-left', custom: null, trigger: { kind: 'always' } },
    objectives: { enabled: true, anchor: 'top-center', custom: null, trigger: { kind: 'always' } },
    laneGold: { enabled: true, anchor: 'top-center', custom: null, trigger: TAB_HELD },
    scoreboardNudge: 0,
  },
  supported: true,
  preview: false,
  shortcut: '⌥⇧O',
  notice: null,
  chords: { 'item-value': { label: 'Tab', warning: null }, 'lane-gold': { label: 'Tab', warning: null } },
  ownBinds: false,
  hud: DEFAULT_HUD,
}
