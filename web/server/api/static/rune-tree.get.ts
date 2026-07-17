import type { RuneTreeResponse, RuneTreeStyle } from '~~/shared/types/static-data'
import {
  buildPerkMap,
  buildPerkStyleMap,
  communityDragonPrefix,
  rewriteCdragonAsset,
  type CdragonPerkRow,
  type CdragonPerkStyleRow,
} from '~~/server/utils/ddragon-loader'

interface PerkStylesResponse {
  styles: Array<CdragonPerkStyleRow & {
    slots: Array<{ type: string, perks: number[] }>
  }>
}

// Normalize patches into the CDragon-friendly `major.minor` form (DDragon
// patches like `15.10.1` reduce to `15.10`). Anything that doesn't match the
// strict numeric format is rejected so a hostile `?patch=` can't traverse out
// of the CDragon prefix; the request falls back to `latest` downstream.
const PATCH_FORMAT_RE = /^\d+\.\d+(?:\.\d+)?$/
function normalizeCdragonPatch(patch: string | null | undefined): string | null {
  if (!patch || !PATCH_FORMAT_RE.test(patch)) return null
  const segments = patch.split('.')
  return `${segments[0]}.${segments[1]}`
}

async function fetchRuneTree(patch: string | null): Promise<RuneTreeResponse> {
  const prefix = communityDragonPrefix(patch)

  const [perks, perkStyles] = await Promise.all([
    $fetch<CdragonPerkRow[]>(`${prefix}/v1/perks.json`),
    $fetch<PerkStylesResponse>(`${prefix}/v1/perkstyles.json`),
  ])

  const perkMap = buildPerkMap(perks, patch)
  const perkStyleMap = buildPerkStyleMap(perkStyles.styles, patch)

  // CommunityDragon emits 7 slots per style: 0 = keystone, 1-3 = regular
  // sub-rows, 4-6 = stat shards (same triplets for every style, so we read
  // them once from any style).
  const styles: RuneTreeStyle[] = perkStyles.styles.map(style => ({
    styleId: style.id,
    name: style.name,
    iconUrl: rewriteCdragonAsset(style.iconPath, patch),
    keystones: style.slots[0]?.perks ?? [],
    subRows: style.slots.slice(1, 4).map(slot => slot.perks),
  }))

  const firstStyle = perkStyles.styles[0]
  const shardSlots = firstStyle ? firstStyle.slots.slice(4, 7).map(slot => slot.perks) : []

  return {
    styles,
    perks: perkMap,
    perkStyles: perkStyleMap,
    shardSlots,
  }
}

const loadRuneTree = defineCachedFunction(
  async (_event, patch: string | null): Promise<RuneTreeResponse> => {
    // CommunityDragon publishes the per-patch directory hours-to-days after
    // DDragon lists the version in versions.json, so early in a patch cycle
    // the pinned URL 404s while `latest` already serves the new data. Fall
    // back to `latest` (icon URLs included) rather than 502ing — a 502 here
    // starves `staticBundleReady` on every profile page. The fallback payload
    // does get cached under the patch key for 1h, which is fine: rune data
    // barely changes between patches and the next miss retries the pinned URL.
    try {
      return await fetchRuneTree(patch)
    }
    catch (patchError) {
      // On errors with `latest` itself, bubble up as 502 — we'd rather fail
      // once and let the next request retry than cache an empty
      // `RuneTreeResponse` for 1h on a transient outage. Same trade-off as
      // champions.get.ts.
      if (patch === null) {
        throw createError({
          statusCode: 502,
          statusMessage: 'CommunityDragon rune tree fetch failed',
          cause: patchError,
        })
      }
      return fetchRuneTree(null).catch((error) => {
        throw createError({
          statusCode: 502,
          statusMessage: 'CommunityDragon rune tree fetch failed',
          cause: error,
        })
      })
    }
  },
  {
    maxAge: 60 * 60,
    name: 'cdragon-rune-tree',
    // Cache key includes the patch so two pages on different patches don't
    // step on each other's cached payload. `latest` keeps the legacy key.
    getKey: (_event, patch: string | null) => `rune-tree:${patch ?? 'latest'}`,
  },
)

export default defineEventHandler(async (event): Promise<RuneTreeResponse> => {
  const { patch } = getQuery(event)
  const normalized = normalizeCdragonPatch(typeof patch === 'string' ? patch : null)
  return loadRuneTree(event, normalized)
})
