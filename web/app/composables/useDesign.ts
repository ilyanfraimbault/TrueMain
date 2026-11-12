/**
 * Design exploration (prototype): which redesign variant this process renders,
 * and how much visual effect the visitor's machine should be asked to carry.
 *
 * The variant comes from `runtimeConfig.public.designVariant`
 * (NUXT_PUBLIC_DESIGN_VARIANT) — one `nuxt dev` per variant, each on its own
 * port. The FX level is decided on the client (`plugins/fx-quality.client.ts`)
 * and can be forced with `?fx=full|lite` for testing.
 */

export type DesignVariant = 'a' | 'b' | 'c' | 'd' | 'e' | 'f' | 'g' | 'h'

export const DESIGN_VARIANTS: Record<DesignVariant, { name: string, tagline: string }> = {
  a: { name: 'Liquid Glass', tagline: 'Refraction, verre liquide, lumière spéculaire' },
  b: { name: 'Spotlight', tagline: 'Lumière au curseur, cartes qui s\'illuminent' },
  c: { name: 'Cinematic', tagline: 'Récit au scroll, typographie géante' },
  d: { name: 'Cockpit', tagline: 'App à sidebar, la home devient un dashboard' },
  e: { name: 'Launcher', tagline: 'Client de jeu : rail d\'icônes, scène champion' },
  f: { name: 'Dock', tagline: 'Recherche d\'abord, dock en bas, mur de champions' },
  g: { name: 'Bento', tagline: 'Grille de tuiles live' },
  h: { name: 'Glass Arena', tagline: 'Carrousel 3D, barre d\'onglets en verre' },
}

function isVariant(raw: unknown): raw is DesignVariant {
  return typeof raw === 'string' && raw in DESIGN_VARIANTS
}

/**
 * The variant this request renders: the `x-tm-design` header the per-port
 * proxy (`scripts/design-proxy.mjs`) adds, else NUXT_PUBLIC_DESIGN_VARIANT.
 * Resolved once during SSR and carried to the client in the payload.
 */
export function useDesignVariant(): DesignVariant | null {
  const state = useState<DesignVariant | null>('design-variant', () => {
    const fromHeader = import.meta.server ? useRequestHeader('x-tm-design') : undefined
    const raw = fromHeader ?? (useRuntimeConfig().public.designVariant as string | undefined)
    return isVariant(raw) ? raw : null
  })
  return state.value
}

/**
 * `full`: every effect, refraction included where the engine can draw it.
 * `lite`: motion and light kept, but no SVG refraction and no continuous
 * canvas loops — for low-core / low-memory machines, data-saver, or reduced
 * motion.
 */
export type FxLevel = 'full' | 'lite'

export interface FxState {
  level: FxLevel
  /** Whether the level was forced by `?fx=` (or the badge) rather than detected. */
  forced: boolean
  /** Chromium draws `backdrop-filter: url(#svg-filter)`; WebKit and Gecko don't. */
  refraction: boolean
  reducedMotion: boolean
  /** Fine pointer with hover — cursor-driven effects are pointless on touch. */
  finePointer: boolean
}

export function useFx() {
  return useState<FxState>('design-fx', () => ({
    level: 'full',
    forced: false,
    refraction: false,
    reducedMotion: false,
    finePointer: true,
  }))
}

export const FX_STORAGE_KEY = 'tm-fx-level'

export function isChromium(): boolean {
  const nav = navigator as Navigator & { userAgentData?: { brands?: { brand: string }[] } }
  return nav.userAgentData?.brands?.some(b => /Chromium/i.test(b.brand)) ?? false
}

/** Force a level from the UI (the variant badge); remembered for the session. */
export function setFxLevel(level: FxLevel) {
  const fx = useFx()
  try {
    sessionStorage.setItem(FX_STORAGE_KEY, level)
  }
  catch {
    // Storage blocked: the level holds until the next load.
  }
  fx.value = { ...fx.value, level, forced: true, refraction: isChromium() && level === 'full' }
}

/** Props every variant's home receives from `pages/index.vue` (the page owns the fetches). */
export interface DesignHomeData {
  overview: import('~~/shared/types/champions').ChampionOverviewResponse | null | undefined
  overviewPending: boolean
  tierlistPending: boolean
  championsById: Map<number, import('~~/shared/types/static-data').ChampionStaticListItem>
  truemainRows: import('~~/shared/types/leaderboard').LeaderboardRowResponse[]
  truemainsInitialLoading: boolean
  trackedTruemains: number | null
  gamesAnalyzed: number
  ddragonPatch: string | null
}
