import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { isLiveChampionId } from '~~/shared/utils/ddragon'
import { normalizeRequestedPatch, resolveLatestDDragonPatch } from '~~/server/utils/ddragon-patch'

/**
 * Synchronised copy of `admin/server/api/static/champions.get.ts` (#1226). The
 * two apps read the same DDragon champion list and must answer it identically;
 * the only intended difference is the admin's `requireUserSession` gate. Keep
 * the rest line-for-line so the next divergence shows up in a diff.
 */

interface ChampionListResponse {
  data: Record<string, { id: string, key: string, name: string, image: { full: string } }>
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
      .filter(item => isLiveChampionId(item.championId))
  },
  {
    maxAge: 60 * 60,
    name: 'ddragon-champion-list',
    getKey: (patch: string) => patch,
  },
)

export default defineEventHandler(async (event): Promise<ChampionStaticListItem[]> => {
  const { patch } = getQuery(event) as { patch?: string }
  // Backend scopes expose patches in the short "16.5" form; DDragon CDN paths
  // need "16.5.1". Normalize (and validate) here so a caller can pass the patch
  // straight from a champion summary. Fall back to the latest DDragon version
  // when none is supplied so a new Riot patch invalidates the cache key
  // naturally.
  const resolved = normalizeRequestedPatch(patch) ?? await resolveLatestDDragonPatch()
  return loadChampionsForPatch(resolved)
})
