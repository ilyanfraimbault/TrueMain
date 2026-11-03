import { IPX_ROUTE_BASE } from '~~/shared/utils/ipx'
import { championSplashUrlFromIcon } from '~~/shared/utils/ddragon'
import { DEFAULT_ELO_BRACKET } from '~~/layers/common/app/utils/elo-brackets'

/**
 * What a web worker asks itself for before the site opens (#2425): the URLs a visitor's first
 * page view sends, so they are the entries a visitor reads.
 */

/** The rendered pages other than a champion's. */
export const WARM_PAGES = ['/', '/champions', '/champions/tierlist', '/truemains', '/builder', '/matchup', '/download', '/about']

/** Renders of each page before the first one a visitor sees: V8 only optimises what has run. */
export const WARM_RENDERS = 3

/** The champion pages rendered, for the same reason: the template is the same for all of them. */
export const WARM_RENDERED_CHAMPIONS = 5

/**
 * What a champion page reads on the server that this worker keeps in memory: its name and its
 * build in words. Asked for directly, every champion costs a fraction of a render.
 */
export function championReads(championId: string, version: string): string[] {
  const patch = version.split('.').slice(0, 2).join('.')
  return [
    `/api/static/${championId}`,
    `/api/static/${championId}?patch=${patch}`,
    `/api/champion-summary/${championId}?eloBracket=${DEFAULT_ELO_BRACKET}`,
  ]
}

/** The patch-less static reads, which give the patch the others are keyed on. */
export const WARM_STATIC_READS = ['/api/static/versions', '/api/static/champions', '/api/static/champion-slugs', '/api/static/rune-tree']

/** The patch-keyed static reads, in both forms the app asks for: a page's patch and the DDragon version. */
export function patchedStaticReads(version: string): string[] {
  const patch = version.split('.').slice(0, 2).join('.')
  return [patch, version].flatMap(value => [
    `/api/static/rune-tree?patch=${value}`,
    `/api/static/items?patch=${value}`,
    `/api/static/summoner-spells?patch=${value}`,
  ])
}

const ICON_MODIFIERS = 'f_webp&s_64x64'
const SPLASH_MODIFIERS = 'w_1280&f_webp'
const LOADING_ART_MODIFIERS = 'w_308&f_webp'
const RANK_CREST_MODIFIERS = /^s_\d{1,3}x\d{1,3}$/

const POSITION_ICONS = ['fill', 'top', 'jungle', 'middle', 'bottom', 'utility']
  .map(position => `positions/icon-position-${position}.png`)

/** Every `iconUrl` in one of the static reads, however deep: the rune tree nests them. */
export function collectIconUrls(value: unknown, found: Set<string> = new Set()): Set<string> {
  if (Array.isArray(value)) {
    for (const entry of value) collectIconUrls(entry, found)
  }
  else if (value && typeof value === 'object') {
    for (const [key, entry] of Object.entries(value)) {
      if (key === 'iconUrl' && typeof entry === 'string' && entry) found.add(entry)
      else collectIconUrls(entry, found)
    }
  }
  return found
}

/**
 * The image proxy's URLs for the catalogue's icons and the champions' banners, as
 * `useCanonicalIcon` and `useChampionSplash` build them.
 */
export function imagePaths(iconUrls: Iterable<string>, championIconUrls: Iterable<string>): string[] {
  const paths = new Set(POSITION_ICONS.map(icon => `${IPX_ROUTE_BASE}/${ICON_MODIFIERS}/${icon}`))
  for (const url of iconUrls) paths.add(`${IPX_ROUTE_BASE}/${ICON_MODIFIERS}/${url}`)
  for (const url of championIconUrls) {
    const splash = championSplashUrlFromIcon(url)
    if (splash) paths.add(`${IPX_ROUTE_BASE}/${SPLASH_MODIFIERS}/${splash}`)
  }
  return [...paths]
}

/**
 * Whether a processed image is worth keeping on disk: one of the shapes the site asks for.
 * The route takes any modifiers from anyone, and a size per request would otherwise fill the
 * budget with images nobody will ask for twice.
 */
export function isDurableImagePath(path: string): boolean {
  const [, base, modifiers] = path.split('/')
  if (`/${base}` !== IPX_ROUTE_BASE || !modifiers) return false
  return modifiers === ICON_MODIFIERS
    || modifiers === SPLASH_MODIFIERS
    || modifiers === LOADING_ART_MODIFIERS
    || RANK_CREST_MODIFIERS.test(modifiers)
}
