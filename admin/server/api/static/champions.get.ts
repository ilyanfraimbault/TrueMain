import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { normalizeDataDragonPatch } from '~~/shared/utils/ddragon'

interface ChampionListResponse {
  data: Record<string, { id: string, key: string, name: string, image: { full: string } }>
}

async function resolveLatestPatch(): Promise<string> {
  const versions = await $fetch<string[]>('https://ddragon.leagueoflegends.com/api/versions.json')
  const latest = versions[0]
  if (!latest) {
    throw createError({ statusCode: 502, statusMessage: 'DDragon returned no versions' })
  }
  return latest
}

// Cached on the resolved patch — not on the raw query param. Without this,
// "?patch=" (the no-patch case) would cache against a fixed key and keep
// serving the previous patch's data after a new patch ships on DDragon.
const loadChampionsForPatch = defineCachedFunction(
  async (patch: string): Promise<ChampionStaticListItem[]> => {
    const champs = await $fetch<ChampionListResponse>(
      `https://ddragon.leagueoflegends.com/cdn/${patch}/data/en_US/champion.json`,
    )

    return Object.values(champs.data)
      .map(champ => ({
        championId: Number(champ.key),
        name: champ.name,
        iconUrl: `https://ddragon.leagueoflegends.com/cdn/${patch}/img/champion/${champ.image.full}`,
      }))
      .filter(item => Number.isFinite(item.championId))
  },
  {
    maxAge: 60 * 60,
    name: 'ddragon-champion-list',
    getKey: (patch: string) => patch,
  },
)

// A DDragon patch is `major.minor.patch` (e.g. "16.5.1"). Validating before the
// value is interpolated into the CDN URL and used as a cache key blocks cache
// poisoning / path injection via an arbitrary `?patch=` param.
const PATCH_PATTERN = /^\d+\.\d+\.\d+$/

export default defineEventHandler(async (event): Promise<ChampionStaticListItem[]> => {
  // Gate behind the operator session like every other ops/static data route.
  await requireUserSession(event)

  const { patch } = getQuery(event) as { patch?: string }
  // Backend scopes expose patches in the short "16.5" form; DDragon CDN paths
  // need "16.5.1". Normalize here so a caller can pass the patch straight from
  // a champion summary. Fall back to the latest DDragon version when none is
  // supplied so a new Riot patch invalidates the cache key naturally.
  const normalized = normalizeDataDragonPatch(patch)
  if (normalized !== null && !PATCH_PATTERN.test(normalized)) {
    throw createError({ statusCode: 400, statusMessage: 'Invalid patch format' })
  }
  const resolved = normalized ?? await resolveLatestPatch()
  return loadChampionsForPatch(resolved)
})
